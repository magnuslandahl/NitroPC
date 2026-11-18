using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NitroPC.Interop;
using NitroPC.Models;
using NitroPC.Services;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using WinForms = System.Windows.Forms;

namespace NitroPC.Views;

public sealed partial class TaskbarStripWindow : Window
{
    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private NitroSettings _settings;
    private NativeMethods.RECT _lastPhysicalBounds;
    private MetricsSnapshot? _latestMetrics;

    public TaskbarStripWindow(NitroSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => Dock();
        Closing += (_, _) => _dockTimer.Stop();
        MouseLeftButtonUp += (_, _) => ToggleFlyoutRequested?.Invoke();
        MouseDown += OnMouseDown;
        MouseRightButtonUp += (_, _) => ContextMenuRequested?.Invoke();
        _dockTimer.Tick += (_, _) => Dock();
    }

    public event Action? ToggleFlyoutRequested;

    public event Action? ToggleKeepAwakeRequested;

    public event Action? ContextMenuRequested;

    public event Action? DockingHidden;

    internal NativeMethods.RECT PhysicalBounds => _lastPhysicalBounds;

    public void UpdateMetrics(MetricsSnapshot metrics)
    {
        _latestMetrics = metrics;
        CpuBars.Values = metrics.CpuCorePercents;
        CpuValue.Text = $"{Math.Round(metrics.CpuTotalPercent):0}%";
        RamSparkline.Values = metrics.RamHistoryPercents;
        RamValue.Text = FormatRamValue(metrics, _settings.RamDisplayMode);
    }

    public void UpdateKeepAwake(KeepAwakeService service)
    {
        var awake = service.IsAwake;
        KeepAwakeBadge.Background = awake
            ? (Brush)FindResource("KaffeGlowFillBrush")
            : Brushes.Transparent;
        KeepAwakeBadge.BorderBrush = awake
            ? (Brush)FindResource("KaffeGlowBorderBrush")
            : Brushes.Transparent;
        KeepAwakeIconPath.Stroke = awake
            ? (Brush)FindResource("KaffeAmberBrush")
            : (Brush)FindResource("KaffeIdleIconBrush");
        KeepAwakeLabel.Foreground = awake
            ? (Brush)FindResource("KaffeLabelAwakeBrush")
            : (Brush)FindResource("TextMutedBrush");
        KeepAwakeValue.Foreground = awake
            ? (Brush)FindResource("KaffeValueAwakeBrush")
            : (Brush)FindResource("TextPrimaryBrush");
        KeepAwakeLabel.Text = awake ? "AWAKE" : "IDLE";
        KeepAwakeValue.Text = awake ? FormatRemaining(service) : "Off";
    }

    public void ApplySettings(NitroSettings settings)
    {
        _settings = settings;
        var hidden = settings.KeepAwakeBarMode == KeepAwakeBarMode.Hidden;
        var compact = settings.KeepAwakeBarMode == KeepAwakeBarMode.CompactIcon;
        KeepAwakeDivider.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        KeepAwakeBadge.Visibility = hidden ? Visibility.Collapsed : Visibility.Visible;
        KeepAwakeText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        KeepAwakeBadge.Padding = compact ? new Thickness(5, 3, 5, 3) : new Thickness(6, 3, 6, 3);

        if (_latestMetrics is not null)
        {
            RamValue.Text = FormatRamValue(_latestMetrics, settings.RamDisplayMode);
        }

        UpdateLayout();
        Dock();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
        _dockTimer.Start();
        Dispatcher.BeginInvoke(Dock, DispatcherPriority.Loaded);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            ToggleKeepAwakeRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void Dock()
    {
        if (!IsLoaded)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        var shellTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        var trayNotify = shellTray == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null);

        NativeMethods.RECT shellRect;
        NativeMethods.RECT notifyRect;
        if (shellTray == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(shellTray, out shellRect))
        {
            var workingArea = WinForms.Screen.PrimaryScreen?.WorkingArea
                ?? WinForms.Screen.AllScreens[0].WorkingArea;
            shellRect = new NativeMethods.RECT
            {
                Left = workingArea.Left,
                Top = workingArea.Bottom - 48,
                Right = workingArea.Right,
                Bottom = workingArea.Bottom
            };
            trayNotify = IntPtr.Zero;
        }

        if (trayNotify == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(trayNotify, out notifyRect))
        {
            notifyRect = shellRect;
            notifyRect.Left = shellRect.Right - 160;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != IntPtr.Zero &&
            foreground != shellTray &&
            foreground != handle &&
            NativeMethods.GetWindowRect(foreground, out var foregroundRect))
        {
            var screen = WinForms.Screen.FromHandle(foreground);
            if (foregroundRect.Left <= screen.Bounds.Left &&
                foregroundRect.Top <= screen.Bounds.Top &&
                foregroundRect.Width >= screen.Bounds.Width &&
                foregroundRect.Height >= screen.Bounds.Height)
            {
                if (Visibility != Visibility.Hidden)
                {
                    Visibility = Visibility.Hidden;
                    DockingHidden?.Invoke();
                }

                return;
            }
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var stripWidthPhysical = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var stripHeightPhysical = shellRect.Height;
        var marginPhysical = (int)Math.Round(6 * dpi.DpiScaleX);
        var targetX = notifyRect.Left - stripWidthPhysical - marginPhysical;
        var targetY = shellRect.Top;

        var rebar = shellTray == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(shellTray, IntPtr.Zero, "ReBarWindow32", null);
        if (rebar != IntPtr.Zero &&
            NativeMethods.GetWindowRect(rebar, out var rebarRect) &&
            targetX < rebarRect.Right + marginPhysical)
        {
            targetX = rebarRect.Right + marginPhysical;
        }

        Height = shellRect.Height / dpi.DpiScaleY;
        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            targetX,
            targetY,
            stripWidthPhysical,
            stripHeightPhysical,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        _lastPhysicalBounds = new NativeMethods.RECT
        {
            Left = targetX,
            Top = targetY,
            Right = targetX + stripWidthPhysical,
            Bottom = targetY + stripHeightPhysical
        };
    }

    private static string FormatRamValue(MetricsSnapshot metrics, RamDisplayMode mode) => mode switch
    {
        RamDisplayMode.Gigabytes => $"{Math.Round(metrics.RamUsedGb):0} GB",
        RamDisplayMode.Both => $"{Math.Round(metrics.RamPercent):0}% · {Math.Round(metrics.RamUsedGb):0}G",
        _ => $"{Math.Round(metrics.RamPercent):0}%"
    };

    private static string FormatRemaining(KeepAwakeService service)
    {
        if (service.IsIndefinite)
        {
            return "∞";
        }

        var remaining = service.Remaining ?? TimeSpan.Zero;
        if (remaining.TotalMinutes <= 90)
        {
            return $"{Math.Ceiling(remaining.TotalMinutes):0}m";
        }

        return $"{remaining.Hours}h {remaining.Minutes}m";
    }
}
