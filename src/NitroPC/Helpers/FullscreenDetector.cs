using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using NitroPC.Interop;

namespace NitroPC.Helpers;

internal static class FullscreenDetector
{
    private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;

    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "TaskListThumbnailWnd",
        "NotifyIconOverflowWindow"
    };

    private static readonly HashSet<string> ShellOverlayProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchApp",
        "ShellExperienceHost",
        "Widgets",
        "TextInputHost",
        "LockApp"
    };

    public static bool IsMonitorCoveredByForeground(
        IntPtr foreground,
        IntPtr shellTray,
        IntPtr stripHandle,
        NativeMethods.RECT monitorBounds)
    {
        if (foreground == IntPtr.Zero ||
            foreground == shellTray ||
            foreground == stripHandle ||
            monitorBounds.Width <= 0 ||
            monitorBounds.Height <= 0 ||
            !NativeMethods.GetWindowRect(foreground, out var foregroundRect) ||
            !CoversMonitor(foregroundRect, monitorBounds))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0 || processId == CurrentProcessId)
        {
            return false;
        }

        if (!NativeMethods.IsWindowVisible(foreground) || IsCloaked(foreground))
        {
            return false;
        }

        var className = ReadWindowClassName(foreground);
        if (ShellWindowClasses.Contains(className))
        {
            return false;
        }

        var processName = TryGetProcessName(processId);
        return ShouldHideTaskbarStrip(
            className,
            processName,
            isVisible: true,
            isCloaked: false,
            foregroundRect,
            monitorBounds);
    }

    internal static bool ShouldHideTaskbarStrip(
        string className,
        string? processName,
        bool isVisible,
        bool isCloaked,
        NativeMethods.RECT windowRect,
        NativeMethods.RECT monitorBounds)
    {
        if (!isVisible ||
            isCloaked ||
            monitorBounds.Width <= 0 ||
            monitorBounds.Height <= 0 ||
            !CoversMonitor(windowRect, monitorBounds))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(className) && ShellWindowClasses.Contains(className))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(processName))
        {
            var cleanProcess = processName.Trim();
            if (cleanProcess.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                cleanProcess = cleanProcess[..^4];
            }

            if (ShellOverlayProcesses.Contains(cleanProcess))
            {
                return false;
            }

            if (cleanProcess.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                !className.Equals("CabinetWClass", StringComparison.OrdinalIgnoreCase) &&
                !className.Equals("ExploreWClass", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool CoversMonitor(
        NativeMethods.RECT windowRect,
        NativeMethods.RECT monitorBounds) =>
        windowRect.Left <= monitorBounds.Left &&
        windowRect.Top <= monitorBounds.Top &&
        windowRect.Right >= monitorBounds.Right &&
        windowRect.Bottom >= monitorBounds.Bottom;

    private static bool IsCloaked(IntPtr hwnd) =>
        NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_CLOAKED,
            out var cloaked,
            sizeof(int)) == 0 &&
        cloaked != 0;

    private static string ReadWindowClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        var length = NativeMethods.GetClassName(hwnd, builder, builder.Capacity);
        return length > 0 ? builder.ToString(0, length) : string.Empty;
    }

    private static string? TryGetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
