using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NitroPC.Helpers;
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
    private readonly CancellationTokenSource _obstacleScanCts = new();
    private readonly NativeMethods.WinEventDelegate _foregroundWinEventCallback;
    private NitroSettings _settings;
    private NativeMethods.RECT _lastPhysicalBounds;
    private MetricsSnapshot? _latestMetrics;
    private IntPtr _foregroundHook;
    private volatile IReadOnlyList<NativeMethods.RECT> _cachedObstacles = Array.Empty<NativeMethods.RECT>();
    private volatile bool _initialObstacleScanDone;

    public TaskbarStripWindow(NitroSettings settings)
    {
        _settings = settings;
        _foregroundWinEventCallback = OnForegroundWindowChanged;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => Dock();
        Closing += OnClosing;
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
        ApplyCpuMetrics(metrics, _settings.CpuDisplayMode);
        CpuValue.Text = $"{Math.Round(metrics.CpuTotalPercent):0}%";
        UpdateCpuSensors(metrics);
        RamSparkline.Values = metrics.RamHistoryPercents;
        RamValue.Text = FormatRamValue(metrics, _settings.RamDisplayMode);
    }

    private void UpdateCpuSensors(MetricsSnapshot metrics)
    {
        var hasTemp = metrics.CpuTemperatureCelsius is not null;
        var hasFreq = metrics.CpuFrequencyGhz is not null;
        var nextVisibility = hasTemp || hasFreq ? Visibility.Visible : Visibility.Collapsed;
        if (CpuSensorPanel.Visibility != nextVisibility)
        {
            CpuSensorPanel.Visibility = nextVisibility;
            UpdateLayout();
            Dock();
        }

        if (metrics.CpuTemperatureCelsius is { } tempC)
        {
            CpuTempValue.Text = $"{Math.Round(tempC):0}°C";
            CpuTempValue.Foreground = tempC >= _settings.AutoQuietThresholdCelsius
                ? (Brush)FindResource("CpuCoralBrush")
                : (Brush)new BrushConverter().ConvertFromString("#FFD6E2E8")!;
        }
        else
        {
            CpuTempValue.Text = string.Empty;
        }

        CpuFreqValue.Text = metrics.CpuFrequencyGhz is { } freqGhz
            ? $"{freqGhz:0.00} GHz"
            : string.Empty;
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
        KeepAwakeBadge.Padding = compact ? new Thickness(6, 0, 6, 0) : new Thickness(7, 0, 7, 0);
        RamValuePanel.Width = settings.RamDisplayMode switch
        {
            RamDisplayMode.Both => 64,
            RamDisplayMode.Gigabytes => 38,
            _ => 32
        };

        CpuBars.IsHistoryMode = settings.CpuDisplayMode == CpuDisplayMode.History;
        CpuBars.HighlightLatestBar = settings.CpuDisplayMode == CpuDisplayMode.History;

        if (_latestMetrics is not null)
        {
            ApplyCpuMetrics(_latestMetrics, settings.CpuDisplayMode);
            UpdateCpuSensors(_latestMetrics);
            RamValue.Text = FormatRamValue(_latestMetrics, settings.RamDisplayMode);
        }
        else
        {
            RamValue.Text = settings.RamDisplayMode switch
            {
                RamDisplayMode.Gigabytes => "0 GB",
                RamDisplayMode.Both => "0% · 0G",
                _ => "0%"
            };
        }

        UpdateLayout();
        Dock();
    }

    private void ApplyCpuMetrics(MetricsSnapshot metrics, CpuDisplayMode mode)
    {
        var history = mode == CpuDisplayMode.History;
        CpuBars.IsHistoryMode = history;
        CpuBars.HighlightLatestBar = history;
        CpuBars.Values = history ? metrics.CpuHistoryPercents : metrics.CpuCorePercents;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _foregroundWinEventCallback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        _dockTimer.Start();
        _ = RunObstacleScanLoopAsync(_obstacleScanCts.Token);
        _ = RevealFallbackAsync(_obstacleScanCts.Token);
    }

    private void OnForegroundWindowChanged(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        Dock();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _dockTimer.Stop();
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        _obstacleScanCts.Cancel();
        _obstacleScanCts.Dispose();
    }

    private async Task RevealFallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            if (!_initialObstacleScanDone)
            {
                _initialObstacleScanDone = true;
                Dock();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunObstacleScanLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do
            {
                await Task.Run(() => RefreshCachedObstacles(), cancellationToken);
                _initialObstacleScanDone = true;
                _ = Dispatcher.BeginInvoke(Dock, DispatcherPriority.Background);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RefreshCachedObstacles()
    {
        if (!TryResolveTaskbarRects(out var shellTray, out var shellRect, out var notifyRect))
        {
            _cachedObstacles = Array.Empty<NativeMethods.RECT>();
            return;
        }

        _cachedObstacles = TaskbarObstacleScanner.Scan(shellTray, shellRect, notifyRect);
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
        if (!IsLoaded || !_initialObstacleScanDone)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        TryResolveTaskbarRects(out var shellTray, out var shellRect, out var notifyRect);

        // Owning the strip to Shell_TrayWnd ensures win32k atomically keeps the strip above the
        // taskbar whenever the user clicks on empty taskbar space (preventing Z-order flicker).
        if (shellTray != IntPtr.Zero &&
            NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWLP_HWNDPARENT) != shellTray)
        {
            NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWLP_HWNDPARENT, shellTray);
        }

        var stripScreen = shellTray != IntPtr.Zero
            ? WinForms.Screen.FromHandle(shellTray)
            : (WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0]);
        var monitorBounds = new NativeMethods.RECT
        {
            Left = stripScreen.Bounds.Left,
            Top = stripScreen.Bounds.Top,
            Right = stripScreen.Bounds.Right,
            Bottom = stripScreen.Bounds.Bottom
        };

        var foreground = NativeMethods.GetForegroundWindow();
        if (FullscreenDetector.IsMonitorCoveredByForeground(
                foreground,
                shellTray,
                handle,
                monitorBounds))
        {
            if (Visibility != Visibility.Hidden)
            {
                Visibility = Visibility.Hidden;
                DockingHidden?.Invoke();
            }

            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var stripWidthPhysical = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var stripHeightPhysical = shellRect.Height;
        var marginPhysical = (int)Math.Round(6 * dpi.DpiScaleX);
        var targetY = shellRect.Top;

        var safeX = TaskbarLayout.FindRightAnchoredSlot(
            shellRect,
            notifyRect,
            _cachedObstacles,
            stripWidthPhysical,
            marginPhysical);
        if (safeX is null)
        {
            if (Visibility != Visibility.Hidden)
            {
                Visibility = Visibility.Hidden;
                DockingHidden?.Invoke();
            }

            return;
        }

        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
        }

        if (RootBorder.Opacity < 1)
        {
            RootBorder.Opacity = 1;
        }

        var targetX = safeX.Value;

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

    private static bool TryResolveTaskbarRects(
        out IntPtr shellTray,
        out NativeMethods.RECT shellRect,
        out NativeMethods.RECT notifyRect)
    {
        shellRect = default;
        shellTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        var trayNotify = shellTray == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null);

        var hasRealShell = shellTray != IntPtr.Zero &&
            NativeMethods.GetWindowRect(shellTray, out shellRect);
        if (!hasRealShell)
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

        return hasRealShell;
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
