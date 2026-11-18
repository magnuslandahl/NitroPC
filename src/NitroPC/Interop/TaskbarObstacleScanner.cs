using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace NitroPC.Interop;

internal static class TaskbarObstacleScanner
{
    private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;

    private static readonly Condition InteractiveTaskbarItemCondition = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.SplitButton),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

    public static IReadOnlyList<NativeMethods.RECT> Scan(
        IntPtr shellTray,
        NativeMethods.RECT shellRect,
        NativeMethods.RECT notifyRect)
    {
        var obstacles = new List<NativeMethods.RECT>();
        var uiaFoundAny = false;

        if (shellTray != IntPtr.Zero)
        {
            uiaFoundAny = TryCollectTaskbarUiElements(shellTray, shellRect, notifyRect, obstacles);

            if (!uiaFoundAny)
            {
                var rebar = NativeMethods.FindWindowEx(shellTray, IntPtr.Zero, "ReBarWindow32", null);
                if (rebar != IntPtr.Zero && NativeMethods.GetWindowRect(rebar, out var rebarRect))
                {
                    AddIfTaskbarObstacle(rebarRect, shellRect, notifyRect, obstacles);
                }
            }
        }

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (handle == shellTray || !NativeMethods.IsWindowVisible(handle))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == CurrentProcessId)
            {
                return true;
            }

            if (NativeMethods.GetWindowRect(handle, out var rect) &&
                rect.Width < shellRect.Width / 2)
            {
                AddIfTaskbarObstacle(rect, shellRect, notifyRect, obstacles);
            }

            return true;
        }, IntPtr.Zero);

        return obstacles;
    }

    private static bool TryCollectTaskbarUiElements(
        IntPtr shellTray,
        NativeMethods.RECT shellRect,
        NativeMethods.RECT notifyRect,
        ICollection<NativeMethods.RECT> obstacles)
    {
        try
        {
            var root = AutomationElement.FromHandle(shellTray);
            var elements = root.FindAll(TreeScope.Descendants, InteractiveTaskbarItemCondition);
            var added = 0;

            for (var index = 0; index < elements.Count; index++)
            {
                try
                {
                    var element = elements[index];
                    if (element.Current.IsOffscreen)
                    {
                        continue;
                    }

                    var bounds = element.Current.BoundingRectangle;
                    if (bounds.IsEmpty ||
                        double.IsNaN(bounds.Left) ||
                        double.IsInfinity(bounds.Left) ||
                        bounds.Width <= 0 ||
                        bounds.Height <= 0)
                    {
                        continue;
                    }

                    var rect = new NativeMethods.RECT
                    {
                        Left = (int)Math.Floor(bounds.Left),
                        Top = (int)Math.Floor(bounds.Top),
                        Right = (int)Math.Ceiling(bounds.Right),
                        Bottom = (int)Math.Ceiling(bounds.Bottom)
                    };

                    if (AddIfTaskbarObstacle(rect, shellRect, notifyRect, obstacles))
                    {
                        added++;
                    }
                }
                catch (ElementNotAvailableException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                catch (COMException)
                {
                }
            }

            return added > 0;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool AddIfTaskbarObstacle(
        NativeMethods.RECT rect,
        NativeMethods.RECT shellRect,
        NativeMethods.RECT notifyRect,
        ICollection<NativeMethods.RECT> obstacles)
    {
        if (rect.Width <= 0 ||
            rect.Height <= 0 ||
            rect.Width >= shellRect.Width - 64 ||
            rect.Left >= notifyRect.Left ||
            rect.Right <= shellRect.Left ||
            rect.Bottom <= shellRect.Top + 4 ||
            rect.Top >= shellRect.Bottom - 4)
        {
            return false;
        }

        obstacles.Add(rect);
        return true;
    }
}
