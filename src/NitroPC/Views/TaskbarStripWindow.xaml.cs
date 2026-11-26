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
    private const double UpgradeHysteresisDip = 8.0;

    private static readonly TaskbarStripDensity[] OrderedDensities =
    [
        TaskbarStripDensity.Full,
        TaskbarStripDensity.CompactGraphs,
        TaskbarStripDensity.ReadoutsOnly,
        TaskbarStripDensity.Mini,
        TaskbarStripDensity.IconOnly
    ];

    private readonly DispatcherTimer _dockTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly CancellationTokenSource _obstacleScanCts = new();
    private readonly NativeMethods.WinEventDelegate _foregroundWinEventCallback;
    private NitroSettings _settings;
    private NativeMethods.RECT _lastPhysicalBounds;
    private MetricsSnapshot? _latestMetrics;
    private IntPtr _foregroundHook;
    private TaskbarStripDensity _activeDensity = TaskbarStripDensity.Full;
    private bool _hasCpuSensors = true;
    private bool _isAwake;
    private string _keepAwakeRemainingText = "Off";
    private bool _isBoostDisabled;
    private bool _isDocking;
    private volatile IReadOnlyList<NativeMethods.RECT> _cachedObstacles = Array.Empty<NativeMethods.RECT>();
    private volatile bool _initialObstacleScanDone;

    public TaskbarStripWindow(NitroSettings settings)
    {
        _settings = settings;
        _foregroundWinEventCallback = OnForegroundWindowChanged;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) =>
        {
            if (!_isDocking)
            {
                Dock();
            }
        };
        Closing += OnClosing;
        MouseLeftButtonUp += (_, _) => ToggleFlyoutRequested?.Invoke();
        MouseDown += OnMouseDown;
        MouseRightButtonUp += (_, _) => ContextMenuRequested?.Invoke();
        _dockTimer.Tick += (_, _) => Dock();
    }

    public event Action? ToggleFlyoutRequested;

    public event Action? ToggleKeepAwakeRequested;

    public event Action? ContextMenuRequested;

    public event Action? FullscreenEntered;

    internal NativeMethods.RECT PhysicalBounds => _lastPhysicalBounds;

    public void UpdateMetrics(MetricsSnapshot metrics)
    {
        _latestMetrics = metrics;
        ApplyCpuMetrics(metrics, _settings.CpuDisplayMode);
        var cpuRounded = Math.Round(metrics.CpuTotalPercent);
        var ramRounded = Math.Round(metrics.RamPercent);
        CpuValue.Text = $"{cpuRounded:0}%";
        MiniCpuValue.Text = $"C {cpuRounded:0}%";
        MiniRamValue.Text = $"R {ramRounded:0}%";
        UpdateCpuSensors(metrics);
        RamSparkline.Values = metrics.RamHistoryPercents;
        UpdateRamReadout();
        RefreshCompactStatusVisuals();
        RefreshToolTip();
    }

    private void UpdateCpuSensors(MetricsSnapshot metrics)
    {
        var hasSensors = metrics.CpuTemperatureCelsius is not null || metrics.CpuFrequencyGhz is not null;
        var sensorsAvailabilityChanged = _hasCpuSensors != hasSensors;
        _hasCpuSensors = hasSensors;

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

        if (sensorsAvailabilityChanged)
        {
            ApplyDensityLayout(_activeDensity);
            Dock();
        }
    }

    public void UpdateKeepAwake(KeepAwakeService service)
    {
        var awake = service.IsAwake;
        _isAwake = awake;
        _keepAwakeRemainingText = awake ? FormatRemaining(service) : "Off";
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
        KeepAwakeValue.Text = _keepAwakeRemainingText;
        RefreshCompactStatusVisuals();
        RefreshToolTip();
    }

    public void UpdateQuietMode(bool boostDisabled)
    {
        _isBoostDisabled = boostDisabled;
        RefreshCompactStatusVisuals();
        RefreshToolTip();
    }

    public void ApplySettings(NitroSettings settings)
    {
        _settings = settings;
        CpuBars.IsHistoryMode = settings.CpuDisplayMode == CpuDisplayMode.History;
        CpuBars.HighlightLatestBar = settings.CpuDisplayMode == CpuDisplayMode.History;

        if (_latestMetrics is not null)
        {
            ApplyCpuMetrics(_latestMetrics, settings.CpuDisplayMode);
            UpdateCpuSensors(_latestMetrics);
        }

        ApplyDensityLayout(_activeDensity);
        UpdateRamReadout();
        RefreshCompactStatusVisuals();
        RefreshToolTip();
        Dock();
    }

    internal static double CalculateExpectedWidthDip(
        TaskbarStripDensity density,
        NitroSettings settings,
        bool hasCpuSensors)
    {
        var keepAwakeHidden = settings.KeepAwakeBarMode == KeepAwakeBarMode.Hidden;
        return density switch
        {
            TaskbarStripDensity.Full =>
                22.0 +
                (44.0 + 7.0 + 32.0 + (hasCpuSensors ? 60.0 : 0.0)) +
                19.0 +
                (42.0 + 7.0 + GetFullRamPanelWidthDip(settings.RamDisplayMode)) +
                settings.KeepAwakeBarMode switch
                {
                    KeepAwakeBarMode.Hidden => 0.0,
                    KeepAwakeBarMode.CompactIcon => 19.0 + 28.0,
                    _ => 19.0 + 84.0
                },
            TaskbarStripDensity.CompactGraphs =>
                18.0 + 83.0 + 15.0 + 81.0 + (keepAwakeHidden ? 0.0 : 43.0),
            TaskbarStripDensity.ReadoutsOnly =>
                18.0 + 32.0 + 15.0 + 32.0 + (keepAwakeHidden ? 0.0 : 43.0),
            TaskbarStripDensity.Mini => 78.0,
            _ => 36.0
        };
    }

    private static double GetFullRamPanelWidthDip(RamDisplayMode mode) => mode switch
    {
        RamDisplayMode.Both => 64.0,
        RamDisplayMode.Gigabytes => 38.0,
        _ => 32.0
    };

    internal double MeasureDensityWidthDipForTesting(TaskbarStripDensity density, bool hasCpuSensors)
    {
        _hasCpuSensors = hasCpuSensors;
        ApplyDensityLayout(density);
        UpdateLayout();
        RootBorder.Measure(new System.Windows.Size(double.PositiveInfinity, 48.0));
        return RootBorder.DesiredSize.Width;
    }

    private void ApplyDensityLayout(TaskbarStripDensity density)
    {
        _activeDensity = density;
        var isStandard = density is TaskbarStripDensity.Full
            or TaskbarStripDensity.CompactGraphs
            or TaskbarStripDensity.ReadoutsOnly;

        StandardStripPanel.Visibility = isStandard ? Visibility.Visible : Visibility.Collapsed;
        MiniStripPanel.Visibility = density == TaskbarStripDensity.Mini ? Visibility.Visible : Visibility.Collapsed;
        IconOnlyPanel.Visibility = density == TaskbarStripDensity.IconOnly ? Visibility.Visible : Visibility.Collapsed;

        RootBorder.Padding = density switch
        {
            TaskbarStripDensity.Full => new Thickness(10, 0, 10, 0),
            TaskbarStripDensity.IconOnly => new Thickness(0),
            _ => new Thickness(8, 0, 8, 0)
        };

        if (isStandard)
        {
            var isFull = density == TaskbarStripDensity.Full;
            var showGraphs = density is TaskbarStripDensity.Full or TaskbarStripDensity.CompactGraphs;
            var dividerMargin = isFull ? new Thickness(9, 0, 9, 0) : new Thickness(7, 0, 7, 0);

            CpuBars.Visibility = showGraphs ? Visibility.Visible : Visibility.Collapsed;
            CpuValueGrid.Margin = showGraphs ? new Thickness(7, 0, 0, 0) : new Thickness(0);
            CpuSensorPanel.Visibility = isFull && _hasCpuSensors ? Visibility.Visible : Visibility.Collapsed;
            CpuRamDivider.Margin = dividerMargin;

            RamSparkline.Visibility = showGraphs ? Visibility.Visible : Visibility.Collapsed;
            RamValuePanel.Margin = showGraphs ? new Thickness(7, 0, 0, 0) : new Thickness(0);
            RamValuePanel.Width = isFull ? GetFullRamPanelWidthDip(_settings.RamDisplayMode) : 32.0;

            var keepAwakeHidden = _settings.KeepAwakeBarMode == KeepAwakeBarMode.Hidden;
            var keepAwakeCompact = !isFull || _settings.KeepAwakeBarMode == KeepAwakeBarMode.CompactIcon;
            KeepAwakeDivider.Visibility = keepAwakeHidden ? Visibility.Collapsed : Visibility.Visible;
            KeepAwakeDivider.Margin = dividerMargin;
            KeepAwakeBadge.Visibility = keepAwakeHidden ? Visibility.Collapsed : Visibility.Visible;
            KeepAwakeText.Visibility = keepAwakeCompact ? Visibility.Collapsed : Visibility.Visible;
            KeepAwakeBadge.Padding = keepAwakeCompact ? new Thickness(6, 0, 6, 0) : new Thickness(7, 0, 7, 0);
        }

        UpdateRamReadout();
        RefreshCompactStatusVisuals();
        Width = CalculateExpectedWidthDip(density, _settings, _hasCpuSensors);
    }

    private void UpdateRamReadout()
    {
        var effectiveMode = _activeDensity == TaskbarStripDensity.Full
            ? _settings.RamDisplayMode
            : RamDisplayMode.Percent;

        if (_latestMetrics is not null)
        {
            RamValue.Text = FormatRamValue(_latestMetrics, effectiveMode);
        }
        else
        {
            RamValue.Text = effectiveMode switch
            {
                RamDisplayMode.Gigabytes => "0 GB",
                RamDisplayMode.Both => "0% · 0G",
                _ => "0%"
            };
        }
    }

    private void RefreshCompactStatusVisuals()
    {
        MiniBrandBadge.Visibility = _isAwake ? Visibility.Collapsed : Visibility.Visible;
        MiniAwakeIcon.Visibility = _isAwake ? Visibility.Visible : Visibility.Collapsed;

        var isHot = _latestMetrics?.CpuTemperatureCelsius is { } tempC &&
            tempC >= _settings.AutoQuietThresholdCelsius;
        if (_isAwake)
        {
            IconStatusDot.Fill = (Brush)FindResource("KaffeAmberBrush");
            IconStatusDot.Visibility = Visibility.Visible;
        }
        else if (isHot)
        {
            IconStatusDot.Fill = (Brush)FindResource("CpuCoralBrush");
            IconStatusDot.Visibility = Visibility.Visible;
        }
        else if (_isBoostDisabled)
        {
            IconStatusDot.Fill = (Brush)FindResource("RamBlueBrush");
            IconStatusDot.Visibility = Visibility.Visible;
        }
        else
        {
            IconStatusDot.Visibility = Visibility.Collapsed;
        }

        var highlightCompactBorder = _isAwake &&
            _activeDensity is TaskbarStripDensity.Mini or TaskbarStripDensity.IconOnly;
        RootBorder.BorderBrush = highlightCompactBorder
            ? (Brush)FindResource("KaffeGlowBorderBrush")
            : (Brush)FindResource("StripBorderBrush");
    }

    private void RefreshToolTip()
    {
        if (_latestMetrics is null)
        {
            StripToolTipText.Text = _isAwake
                ? $"NitroPC — Awake ({_keepAwakeRemainingText})"
                : "NitroPC — Click to open toolbox";
            return;
        }

        var parts = new List<string>(4)
        {
            $"CPU {Math.Round(_latestMetrics.CpuTotalPercent):0}%"
        };
        if (_latestMetrics.CpuTemperatureCelsius is { } tempC)
        {
            parts[0] += $" · {Math.Round(tempC):0}°C";
        }

        if (_latestMetrics.CpuFrequencyGhz is { } ghz)
        {
            parts[0] += $" · {ghz:0.00} GHz";
        }

        parts.Add($"RAM {Math.Round(_latestMetrics.RamPercent):0}% ({_latestMetrics.RamUsedGb:0.0}/{_latestMetrics.RamTotalGb:0.0} GB)");
        if (_isAwake)
        {
            parts.Add($"Awake {_keepAwakeRemainingText}");
        }

        if (_isBoostDisabled)
        {
            parts.Add("Quiet boost paused");
        }

        StripToolTipText.Text = $"NitroPC — {string.Join("  |  ", parts)}";
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
        if (_isDocking || !IsLoaded || !_initialObstacleScanDone)
        {
            return;
        }

        _isDocking = true;
        try
        {
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
                    FullscreenEntered?.Invoke();
                }

                return;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            var candidates = new TaskbarDensityCandidate[OrderedDensities.Length];
            for (var index = 0; index < OrderedDensities.Length; index++)
            {
                var density = OrderedDensities[index];
                var widthDip = CalculateExpectedWidthDip(density, _settings, _hasCpuSensors);
                candidates[index] = new TaskbarDensityCandidate(
                    density,
                    (int)Math.Ceiling(widthDip * dpi.DpiScaleX));
            }

            var stripHeightPhysical = shellRect.Height;
            var marginPhysical = (int)Math.Round(6 * dpi.DpiScaleX);
            var upgradeHysteresisPhysical = (int)Math.Round(UpgradeHysteresisDip * dpi.DpiScaleX);
            var currentVisibleDensity = Visibility == Visibility.Visible
                ? _activeDensity
                : (TaskbarStripDensity?)null;

            var placement = TaskbarLayout.SelectResponsiveSlot(
                shellRect,
                notifyRect,
                _cachedObstacles,
                candidates,
                marginPhysical,
                currentVisibleDensity,
                upgradeHysteresisPhysical);
            if (placement is null)
            {
                if (Visibility != Visibility.Hidden)
                {
                    Visibility = Visibility.Hidden;
                }

                return;
            }

            if (_activeDensity != placement.Value.Density)
            {
                ApplyDensityLayout(placement.Value.Density);
                UpdateLayout();
            }

            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
            }

            if (RootBorder.Opacity < 1)
            {
                RootBorder.Opacity = 1;
            }

            var targetX = placement.Value.Left;
            var targetY = shellRect.Top;
            var stripWidthPhysical = placement.Value.Width;

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
        finally
        {
            _isDocking = false;
        }
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
