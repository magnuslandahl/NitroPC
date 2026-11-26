using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NitroPC.Interop;
using NitroPC.Models;
using NitroPC.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfBrushes = System.Windows.Media.Brushes;
using WinForms = System.Windows.Forms;

namespace NitroPC.Views;

public sealed partial class ToolboxFlyoutWindow : Window
{
    private sealed record ProcessRowElements(
        Grid Row,
        TextBlock NameText,
        TextBlock CountText,
        Border BarFill,
        TextBlock PercentText);

    private const double CollapsedCardOuterWidthDip = 384.0; // 8 left margin + 368 card + 8 right shadow
    private const double SettingsDrawerExpandedWidthDip = 300.0;

    private readonly NitroSettings _settings;
    private readonly List<ProcessRowElements> _processRows = new(5);
    private MetricsSnapshot? _latestMetrics;
    private NativeMethods.RECT? _lastStripPhysicalRect;
    private DateTime _lastOpenedFromTrayUtc = DateTime.MinValue;
    private QuietModeLevel _quietModeLevel;
    private bool _isBoostDisabled;
    private bool _isAwake;
    private bool _isSettingsOpen;

    public ToolboxFlyoutWindow(NitroSettings settings)
    {
        _settings = settings;
        _settings.Normalize();
        _quietModeLevel = _settings.QuietMode;
        InitializeComponent();
        InitializeProcessRows();
        var versionLabel = $"v{UpdateService.CurrentVersionText}";
        HeaderVersionText.Text = versionLabel;
        VersionText.Text = versionLabel;
        StartupToggle.IsChecked = new StartupService().IsEnabled();
        AutomaticUpdatesToggle.IsChecked = _settings.AutomaticallyCheckForUpdates;
        ApplyInitialCpuMode();
        PreviewKeyDown += OnPreviewKeyDown;
        ApplySelectionStyles();
        RefreshQuietModeSummary();
    }

    public DateTime LastDeactivatedUtc { get; private set; } = DateTime.MinValue;

    public event Action<NitroSettings>? SettingsChanged;

    public event Action<TimeSpan?>? KeepAwakeStartRequested;

    public event Action? KeepAwakeStopRequested;

    public event Action<QuietModeLevel>? QuietModeChangedRequested;

    public event Action<int>? AutoQuietThresholdChangedRequested;

    public event Action<bool>? StartupChangedRequested;

    public event Action? QuitRequested;

    public event Action? CheckForUpdatesRequested;

    public void UpdateMetrics(MetricsSnapshot metrics)
    {
        var previousProcessCount = TopProcessesPanel.Visibility == Visibility.Visible
            ? _processRows.Count(row => row.Row.Visibility == Visibility.Visible)
            : 0;

        _latestMetrics = metrics;
        CpuLargeValue.Text = $"{Math.Round(metrics.CpuTotalPercent):0}%";
        CpuSensorsText.Text = FormatCpuSensors(metrics);
        CpuSensorsText.Foreground = metrics.CpuTemperatureCelsius is { } tempC &&
            tempC >= _settings.AutoQuietThresholdCelsius
            ? (Brush)FindResource("CpuCoralBrush")
            : (Brush)FindResource("TextPrimaryBrush");
        ApplyCpuDisplay(metrics);
        RamLargeValue.Text = $"{Math.Round(metrics.RamPercent):0}%";
        RamUsageText.Text = $"{metrics.RamUsedGb:0.0} / {metrics.RamTotalGb:0.0} GB";
        RamLargeSparkline.Values = metrics.RamHistoryPercents;
        UpdateTopProcesses(metrics.TopProcesses);
        RefreshQuietModeSummary();

        var newProcessCount = TopProcessesPanel.Visibility == Visibility.Visible
            ? _processRows.Count(row => row.Row.Visibility == Visibility.Visible)
            : 0;
        if (IsVisible && newProcessCount != previousProcessCount && _lastStripPhysicalRect is { } stripRect)
        {
            SizeToContent = SizeToContent.Height;
            InvalidateMeasure();
            UpdateLayout();
            PositionNearStrip(stripRect);
        }
    }

    private void ApplyInitialCpuMode()
    {
        var history = _settings.CpuDisplayMode == CpuDisplayMode.History;
        CpuLargeBars.IsHistoryMode = history;
        CpuLargeBars.HighlightLatestBar = history;
        CpuCoresText.Text = history
            ? "60-second history"
            : $"{Environment.ProcessorCount} logical cores";
    }

    private void ApplyCpuDisplay(MetricsSnapshot metrics)
    {
        var history = _settings.CpuDisplayMode == CpuDisplayMode.History;
        CpuLargeBars.IsHistoryMode = history;
        CpuLargeBars.HighlightLatestBar = history;
        CpuLargeBars.Values = history
            ? metrics.CpuHistoryPercents
            : metrics.CpuCorePercents;
        CpuCoresText.Text = history
            ? "60-second history"
            : metrics.LogicalCoreCount > 30
                ? $"{metrics.LogicalCoreCount} logical cores, grouped"
                : $"{metrics.LogicalCoreCount} logical cores";
    }

    public void UpdateKeepAwake(KeepAwakeService service)
    {
        var awake = service.IsAwake;
        _isAwake = awake;
        StatusChip.Visibility = awake ? Visibility.Visible : Visibility.Collapsed;
        if (awake)
        {
            StatusText.Text = service.IsIndefinite
                ? "Awake · ∞"
                : $"Awake · {FormatCountdown(service.Remaining ?? TimeSpan.Zero)}";
        }

        KeepAwakeDetailText.Text = awake
            ? "Prevents sleep, screen lock & screensaver"
            : "Off — normal sleep & screen lock";
        KeepAwakeButton.Content = awake ? "Stop" : "Start";
        KeepAwakeButton.Background = awake
            ? (Brush)FindResource("KaffeAmberBrush")
            : (Brush)new BrushConverter().ConvertFromString("#1AFFFFFF")!;
        KeepAwakeButton.Foreground = awake
            ? (Brush)new BrushConverter().ConvertFromString("#FF171205")!
            : (Brush)FindResource("TextPrimaryBrush");
        KeepAwakeCard.BorderBrush = awake
            ? (Brush)FindResource("KaffeGlowBorderBrush")
            : (Brush)FindResource("CardBorderBrush");
        KeepAwakeProgress.Value = !awake
            ? 0
            : service.IsIndefinite || service.TotalDuration is not { } total || total <= TimeSpan.Zero
                ? 1
                : Math.Clamp(
                    (service.Remaining ?? TimeSpan.Zero).TotalSeconds / total.TotalSeconds,
                    0,
                    1);
        ApplyPresetStyles();
    }

    public void SetStartupEnabled(bool enabled)
    {
        if (StartupToggle.IsChecked == enabled)
        {
            return;
        }

        StartupToggle.IsChecked = enabled;
    }

    public void UpdateQuietMode(QuietModeLevel mode, bool boostDisabled, int thresholdCelsius)
    {
        _quietModeLevel = mode;
        _isBoostDisabled = boostDisabled;
        _settings.AutoQuietThresholdCelsius = NormalizeAutoThreshold(thresholdCelsius);
        ApplySelectionStyles();
        RefreshQuietModeSummary();
    }

    private void RefreshQuietModeSummary()
    {
        var threshold = _settings.AutoQuietThresholdCelsius;
        var resume = threshold - QuietModeService.HysteresisBandCelsius;
        var tempLabel = _latestMetrics?.CpuTemperatureCelsius is { } tempC
            ? $" · {Math.Round(tempC):0}°C"
            : string.Empty;

        QuietModeDetailText.Text = _quietModeLevel switch
        {
            QuietModeLevel.Quiet => $"Always quiet — CPU turbo boost disabled{tempLabel}",
            QuietModeLevel.Auto when _isBoostDisabled =>
                $"Auto — boost paused{tempLabel} (resumes ≤ {resume}°C)",
            QuietModeLevel.Auto =>
                $"Auto (< {threshold}°C) — boost active{tempLabel}",
            _ => "Normal CPU boost — full peak clock speed"
        };

        QuietModeCard.BorderBrush = _isBoostDisabled
            ? (Brush)FindResource("RamBlueBrush")
            : _quietModeLevel == QuietModeLevel.Auto
                ? (Brush)FindResource("DividerBrush")
                : (Brush)FindResource("CardBorderBrush");
    }

    public void SetUpdateStatus(string text)
    {
        UpdateStatusText.Text = text;
        UpdateStatusText.Visibility = string.IsNullOrWhiteSpace(text)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    internal void ToggleNearStrip(NativeMethods.RECT stripPhysicalRect)
    {
        if (stripPhysicalRect.Width <= 0 || stripPhysicalRect.Height <= 0)
        {
            ToggleFromTray(WinForms.Cursor.Position);
            return;
        }

        if (IsVisible)
        {
            SetSettingsDrawerOpen(false, animate: false);
            Hide();
            return;
        }

        if ((DateTime.UtcNow - LastDeactivatedUtc).TotalMilliseconds < 180)
        {
            return;
        }

        SetSettingsDrawerOpen(false, animate: false);
        _lastStripPhysicalRect = stripPhysicalRect;
        Show();
        UpdateLayout();
        PositionNearStrip(stripPhysicalRect, animateHorizontal: false);
        BringToFrontAndActivate();
    }

    internal void ToggleFromTray(System.Drawing.Point clickPhysicalPoint)
    {
        if (IsVisible)
        {
            // Keep open and focused if the user double-clicked the notification area icon.
            if ((DateTime.UtcNow - _lastOpenedFromTrayUtc).TotalMilliseconds < 400)
            {
                BringToFrontAndActivate();
                return;
            }

            SetSettingsDrawerOpen(false, animate: false);
            Hide();
            return;
        }

        if ((DateTime.UtcNow - LastDeactivatedUtc).TotalMilliseconds < 180)
        {
            return;
        }

        _lastOpenedFromTrayUtc = DateTime.UtcNow;
        SetSettingsDrawerOpen(false, animate: false);
        Show();
        UpdateLayout();
        var anchorRect = BuildTrayAnchorRect(clickPhysicalPoint);
        _lastStripPhysicalRect = anchorRect;
        PositionNearStrip(anchorRect, animateHorizontal: false);
        BringToFrontAndActivate();
    }

    private NativeMethods.RECT BuildTrayAnchorRect(System.Drawing.Point clickPoint)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = WinForms.Screen.FromPoint(clickPoint);
        var collapsedWidthPhysical = (int)Math.Ceiling(CollapsedCardOuterWidthDip * dpi.DpiScaleX);

        var taskbarTop = screen.WorkingArea.Bottom;
        if (taskbarTop >= screen.Bounds.Bottom)
        {
            var shellTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (shellTray != IntPtr.Zero &&
                NativeMethods.GetWindowRect(shellTray, out var shellRect) &&
                shellRect.Top > screen.Bounds.Top + screen.Bounds.Height / 2 &&
                shellRect.Top < screen.Bounds.Bottom &&
                shellRect.Right > screen.Bounds.Left &&
                shellRect.Left < screen.Bounds.Right)
            {
                taskbarTop = shellRect.Top;
            }
            else if (screen.WorkingArea.Top == screen.Bounds.Top)
            {
                taskbarTop = screen.Bounds.Bottom - (int)Math.Round(48 * dpi.DpiScaleY);
            }
        }

        var halfWidth = collapsedWidthPhysical / 2;
        return new NativeMethods.RECT
        {
            Left = clickPoint.X - halfWidth,
            Top = taskbarTop,
            Right = clickPoint.X + halfWidth,
            Bottom = screen.Bounds.Bottom
        };
    }

    private void BringToFrontAndActivate()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(hwnd);
        }

        Activate();
    }

    private void PositionNearStrip(NativeMethods.RECT stripPhysicalRect, bool animateHorizontal = false)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var visualWidthDip = CollapsedCardOuterWidthDip +
            (_isSettingsOpen ? SettingsDrawerExpandedWidthDip : 0.0);
        var collapsedWidthPhysical = (int)Math.Ceiling(CollapsedCardOuterWidthDip * dpi.DpiScaleX);
        var activeWidthPhysical = (int)Math.Ceiling(visualWidthDip * dpi.DpiScaleX);
        var heightPhysical = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var screenPoint = new System.Drawing.Point(stripPhysicalRect.Right, stripPhysicalRect.Top);
        var screen = WinForms.Screen.FromPoint(screenPoint);
        var marginPhysical = (int)Math.Round(8 * dpi.DpiScaleX);

        // Anchor the main Toolbox column above the strip so opening Settings expands cleanly to the right,
        // shifting left only if the expanded drawer would cross the monitor's right edge.
        var baseX = stripPhysicalRect.Right - collapsedWidthPhysical;
        var x = Math.Max(
            screen.WorkingArea.Left + marginPhysical,
            Math.Min(baseX, screen.WorkingArea.Right - activeWidthPhysical - marginPhysical));
        var y = Math.Max(
            screen.WorkingArea.Top + marginPhysical,
            stripPhysicalRect.Top - heightPhysical - (int)Math.Round(10 * dpi.DpiScaleY));

        var targetLeftDip = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;

        BeginAnimation(LeftProperty, null);
        if (animateHorizontal && Math.Abs(Left - targetLeftDip) > 0.5)
        {
            var leftAnimation = new DoubleAnimation(Left, targetLeftDip, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(LeftProperty, leftAnimation);
        }
        else
        {
            Left = targetLeftDip;
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        // When opening from Windows 11's "Show hidden icons" overflow popup, explorer.exe
        // closes TopLevelWindowForOverflowXamlIsland right after WM_LBUTTONUP and momentarily
        // transfers focus back to Shell_TrayWnd. Reclaim focus instead of closing immediately.
        if ((DateTime.UtcNow - _lastOpenedFromTrayUtc).TotalMilliseconds < 350 &&
            IsShellTrayOrOverflowForeground())
        {
            _ = Dispatcher.BeginInvoke(
                () =>
                {
                    if (IsVisible)
                    {
                        BringToFrontAndActivate();
                    }
                },
                DispatcherPriority.Input);
            base.OnDeactivated(e);
            return;
        }

        LastDeactivatedUtc = DateTime.UtcNow;
        SetSettingsDrawerOpen(false, animate: false);
        Hide();
        base.OnDeactivated(e);
    }

    private static bool IsShellTrayOrOverflowForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return true;
        }

        var builder = new StringBuilder(128);
        if (NativeMethods.GetClassName(foreground, builder, builder.Capacity) <= 0)
        {
            return false;
        }

        var className = builder.ToString();
        return string.Equals(className, "Shell_TrayWnd", StringComparison.Ordinal) ||
            string.Equals(className, "TopLevelWindowForOverflowXamlIsland", StringComparison.Ordinal) ||
            string.Equals(className, "NotifyIconOverflowWindow", StringComparison.Ordinal) ||
            string.Equals(className, "XamlExplorerHostIslandWindow", StringComparison.Ordinal);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isSettingsOpen)
            {
                SetSettingsDrawerOpen(false, animate: true);
            }
            else
            {
                Hide();
            }

            e.Handled = true;
        }
    }

    private void OnToggleKeepAwake(object sender, RoutedEventArgs e)
    {
        if (KeepAwakeButton.Content?.ToString() == "Stop")
        {
            KeepAwakeStopRequested?.Invoke();
            return;
        }

        var minutes = NormalizePresetMinutes(_settings.KeepAwakeMinutes);
        _settings.KeepAwakeMinutes = minutes;
        SettingsChanged?.Invoke(_settings);
        KeepAwakeStartRequested?.Invoke(minutes == 0 ? null : TimeSpan.FromMinutes(minutes));
        ApplyPresetStyles();
    }

    private void OnSettingsToggle(object sender, RoutedEventArgs e)
    {
        SetSettingsDrawerOpen(!_isSettingsOpen, animate: true);
    }

    private void OnCloseSettingsClicked(object sender, RoutedEventArgs e)
    {
        SetSettingsDrawerOpen(false, animate: true);
    }

    internal void SetSettingsDrawerOpen(bool open, bool animate)
    {
        _isSettingsOpen = open;
        SettingsButton.Background = open
            ? (Brush)FindResource("RamFillBrush")
            : (Brush)FindResource("CardBackgroundBrush");
        SettingsButton.BorderBrush = open
            ? (Brush)FindResource("RamBlueBrush")
            : (Brush)FindResource("CardBorderBrush");
        SettingsIconPath.Stroke = open
            ? (Brush)FindResource("TextPrimaryBrush")
            : (Brush)FindResource("TextMutedBrush");

        var targetWidth = open ? SettingsDrawerExpandedWidthDip : 0.0;
        var targetOpacity = open ? 1.0 : 0.0;
        var targetAngle = open ? 60.0 : 0.0;

        SettingsDrawer.BeginAnimation(WidthProperty, null);
        SettingsDrawerContent.BeginAnimation(OpacityProperty, null);
        SettingsGearRotate.BeginAnimation(RotateTransform.AngleProperty, null);

        if (animate && IsVisible)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            SettingsDrawer.BeginAnimation(
                WidthProperty,
                new DoubleAnimation(SettingsDrawer.ActualWidth, targetWidth, new Duration(TimeSpan.FromMilliseconds(220)))
                {
                    EasingFunction = ease
                });
            SettingsDrawerContent.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(SettingsDrawerContent.Opacity, targetOpacity, new Duration(TimeSpan.FromMilliseconds(180)))
                {
                    EasingFunction = ease
                });
            SettingsGearRotate.BeginAnimation(
                RotateTransform.AngleProperty,
                new DoubleAnimation(SettingsGearRotate.Angle, targetAngle, new Duration(TimeSpan.FromMilliseconds(220)))
                {
                    EasingFunction = ease
                });

            if (_lastStripPhysicalRect is { } stripRect)
            {
                PositionNearStrip(stripRect, animateHorizontal: true);
            }
        }
        else
        {
            SettingsDrawer.Width = targetWidth;
            SettingsDrawerContent.Opacity = targetOpacity;
            SettingsGearRotate.Angle = targetAngle;

            if (IsVisible)
            {
                SizeToContent = SizeToContent.Height;
                InvalidateMeasure();
                UpdateLayout();
                if (_lastStripPhysicalRect is { } stripRect)
                {
                    PositionNearStrip(stripRect, animateHorizontal: false);
                }
            }
        }
    }

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var minutes = int.Parse((string)button.Tag, CultureInfo.InvariantCulture);
        _settings.KeepAwakeMinutes = minutes;
        SettingsChanged?.Invoke(_settings);
        KeepAwakeStartRequested?.Invoke(minutes == 0 ? null : TimeSpan.FromMinutes(minutes));
        ApplyPresetStyles();
    }

    private void OnRamModeClicked(object sender, RoutedEventArgs e)
    {
        var mode = Enum.Parse<RamDisplayMode>((string)((Button)sender).Tag);
        _settings.RamDisplayMode = mode;
        SettingsChanged?.Invoke(_settings);
        ApplySelectionStyles();
    }

    private void OnCpuModeClicked(object sender, RoutedEventArgs e)
    {
        var mode = Enum.Parse<CpuDisplayMode>((string)((Button)sender).Tag);
        _settings.CpuDisplayMode = mode;
        SettingsChanged?.Invoke(_settings);
        if (_latestMetrics is not null)
        {
            ApplyCpuDisplay(_latestMetrics);
        }

        ApplySelectionStyles();
    }

    private void OnBarModeClicked(object sender, RoutedEventArgs e)
    {
        var mode = Enum.Parse<KeepAwakeBarMode>((string)((Button)sender).Tag);
        _settings.KeepAwakeBarMode = mode;
        SettingsChanged?.Invoke(_settings);
        ApplySelectionStyles();
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        StartupChangedRequested?.Invoke(StartupToggle.IsChecked == true);
    }

    private void OnQuietModeClicked(object sender, RoutedEventArgs e)
    {
        var mode = Enum.Parse<QuietModeLevel>((string)((Button)sender).Tag);
        _settings.QuietMode = mode;
        SettingsChanged?.Invoke(_settings);
        QuietModeChangedRequested?.Invoke(mode);
    }

    private void OnAutoThresholdClicked(object sender, RoutedEventArgs e)
    {
        var threshold = int.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture);
        _settings.AutoQuietThresholdCelsius = NormalizeAutoThreshold(threshold);
        SettingsChanged?.Invoke(_settings);
        AutoQuietThresholdChangedRequested?.Invoke(_settings.AutoQuietThresholdCelsius);
        if (_latestMetrics?.CpuTemperatureCelsius is { } tempC)
        {
            CpuSensorsText.Foreground = tempC >= _settings.AutoQuietThresholdCelsius
                ? (Brush)FindResource("CpuCoralBrush")
                : (Brush)FindResource("TextPrimaryBrush");
        }

        ApplySelectionStyles();
        RefreshQuietModeSummary();
    }

    private void OnAutomaticUpdatesChanged(object sender, RoutedEventArgs e)
    {
        _settings.AutomaticallyCheckForUpdates = AutomaticUpdatesToggle.IsChecked == true;
        SettingsChanged?.Invoke(_settings);
    }

    private void OnCheckUpdatesClicked(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesRequested?.Invoke();
    }

    private void OnQuitClicked(object sender, RoutedEventArgs e)
    {
        QuitRequested?.Invoke();
    }

    private void ApplySelectionStyles()
    {
        SetSelected(CpuCoresButton, _settings.CpuDisplayMode == CpuDisplayMode.Cores);
        SetSelected(CpuHistoryButton, _settings.CpuDisplayMode == CpuDisplayMode.History);
        SetSelected(RamPercentButton, _settings.RamDisplayMode == RamDisplayMode.Percent);
        SetSelected(RamGigabytesButton, _settings.RamDisplayMode == RamDisplayMode.Gigabytes);
        SetSelected(RamBothButton, _settings.RamDisplayMode == RamDisplayMode.Both);
        SetSelected(BarBadgeButton, _settings.KeepAwakeBarMode == KeepAwakeBarMode.Badge);
        SetSelected(BarIconButton, _settings.KeepAwakeBarMode == KeepAwakeBarMode.CompactIcon);
        SetSelected(BarHiddenButton, _settings.KeepAwakeBarMode == KeepAwakeBarMode.Hidden);
        SetSelected(QuietOffButton, _quietModeLevel == QuietModeLevel.Off);
        SetSelected(QuietAutoButton, _quietModeLevel == QuietModeLevel.Auto);
        SetSelected(QuietAlwaysButton, _quietModeLevel == QuietModeLevel.Quiet);

        var threshold = NormalizeAutoThreshold(_settings.AutoQuietThresholdCelsius);
        SetSelected(AutoTemp60Button, threshold == 60);
        SetSelected(AutoTemp70Button, threshold == 70);
        SetSelected(AutoTemp80Button, threshold == 80);
        ApplyPresetStyles();
    }

    private void ApplyPresetStyles()
    {
        var activeMinutes = NormalizePresetMinutes(_settings.KeepAwakeMinutes);
        SetPresetSelected(Preset1hButton, activeMinutes == 60);
        SetPresetSelected(Preset2hButton, activeMinutes == 120);
        SetPresetSelected(Preset4hButton, activeMinutes == 240);
        SetPresetSelected(Preset8hButton, activeMinutes == 480);
        SetPresetSelected(PresetInfiniteButton, activeMinutes == 0);
    }

    private void SetPresetSelected(Button button, bool selected)
    {
        if (!selected)
        {
            button.Background = (Brush)FindResource("CardBackgroundBrush");
            button.Foreground = (Brush)FindResource("TextMutedBrush");
            button.BorderBrush = (Brush)FindResource("CardBorderBrush");
            return;
        }

        button.Background = _isAwake
            ? (Brush)FindResource("KaffeGlowFillBrush")
            : (Brush)FindResource("ControlHoverBrush");
        button.Foreground = _isAwake
            ? (Brush)FindResource("KaffeValueAwakeBrush")
            : (Brush)FindResource("TextPrimaryBrush");
        button.BorderBrush = _isAwake
            ? (Brush)FindResource("KaffeGlowBorderBrush")
            : (Brush)FindResource("DividerBrush");
    }

    private void SetSelected(Button button, bool selected)
    {
        button.Background = selected
            ? (Brush)FindResource("RamFillBrush")
            : (Brush)FindResource("CardBackgroundBrush");
        button.Foreground = selected
            ? (Brush)FindResource("TextPrimaryBrush")
            : (Brush)FindResource("TextMutedBrush");
        button.BorderBrush = selected
            ? (Brush)FindResource("RamBlueBrush")
            : (Brush)FindResource("CardBorderBrush");
    }

    internal static int NormalizePresetMinutes(int minutes) =>
        NitroSettings.NormalizePresetMinutes(minutes);

    internal static int NormalizeAutoThreshold(int thresholdCelsius) =>
        NitroSettings.NormalizeAutoQuietThreshold(thresholdCelsius);

    private void InitializeProcessRows()
    {
        var font = (System.Windows.Media.FontFamily)FindResource("NitroFont");
        var primaryBrush = (Brush)FindResource("TextPrimaryBrush");
        var mutedBrush = (Brush)FindResource("TextMutedBrush");
        var coralBrush = (Brush)FindResource("CpuCoralBrush");
        var trackBrush = (Brush)new BrushConverter().ConvertFromString("#17FFFFFF")!;

        for (var index = 0; index < 5; index++)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, index == 0 ? 0 : 4, 0, 0),
                Visibility = Visibility.Collapsed
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

            var nameStack = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var nameText = new TextBlock
            {
                FontFamily = font,
                FontSize = 11,
                Foreground = primaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 175,
                VerticalAlignment = VerticalAlignment.Center
            };
            var countText = new TextBlock
            {
                FontFamily = font,
                FontSize = 10,
                Foreground = mutedBrush,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            nameStack.Children.Add(nameText);
            nameStack.Children.Add(countText);

            var barTrack = new Border
            {
                Width = 52,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = trackBrush,
                Margin = new Thickness(8, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var barFill = new Border
            {
                Width = 0,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = coralBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };
            barTrack.Child = barFill;
            Grid.SetColumn(barTrack, 1);

            var percentText = new TextBlock
            {
                FontFamily = font,
                FontSize = 11,
                FontWeight = index == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = index == 0 ? primaryBrush : mutedBrush,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Documents.Typography.SetNumeralAlignment(percentText, FontNumeralAlignment.Tabular);
            Grid.SetColumn(percentText, 2);

            row.Children.Add(nameStack);
            row.Children.Add(barTrack);
            row.Children.Add(percentText);

            TopProcessesPanel.Children.Add(row);
            _processRows.Add(new ProcessRowElements(row, nameText, countText, barFill, percentText));
        }
    }

    private void UpdateTopProcesses(IReadOnlyList<ProcessUsageItem> topProcesses)
    {
        if (topProcesses.Count == 0)
        {
            if (_latestMetrics is { CpuHistoryPercents.Length: > 1 })
            {
                TopProcessesEmptyText.Text = "System idle (< 0.1% per process)";
            }

            TopProcessesEmptyText.Visibility = Visibility.Visible;
            TopProcessesPanel.Visibility = Visibility.Collapsed;
            return;
        }

        TopProcessesEmptyText.Visibility = Visibility.Collapsed;
        TopProcessesPanel.Visibility = Visibility.Visible;

        var scaleMax = Math.Max(10.0, topProcesses[0].CpuPercent);
        for (var index = 0; index < _processRows.Count; index++)
        {
            var elements = _processRows[index];
            if (index >= topProcesses.Count)
            {
                elements.Row.Visibility = Visibility.Collapsed;
                continue;
            }

            var item = topProcesses[index];
            elements.Row.Visibility = Visibility.Visible;
            elements.NameText.Text = item.Name;
            elements.CountText.Text = item.InstanceCount > 1 ? $"({item.InstanceCount})" : string.Empty;
            elements.BarFill.Width = Math.Clamp(item.CpuPercent / scaleMax, 0.06, 1.0) * 52.0;
            elements.PercentText.Text = $"{item.CpuPercent:0.0}%";
        }
    }

    private static string FormatCpuSensors(MetricsSnapshot metrics) =>
        (metrics.CpuFrequencyGhz, metrics.CpuTemperatureCelsius) switch
        {
            ({ } ghz, { } temp) => $"{ghz:0.00} GHz · {Math.Round(temp):0}°C",
            ({ } ghz, null) => $"{ghz:0.00} GHz",
            (null, { } temp) => $"{Math.Round(temp):0}°C",
            _ => "Sensors unavailable"
        };

    private static string FormatCountdown(TimeSpan remaining) =>
        $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
}
