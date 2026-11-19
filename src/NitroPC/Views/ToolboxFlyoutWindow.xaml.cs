using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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

    private static readonly int[] SupportedPresets = [60, 120, 240, 480, 0];
    private static readonly int[] SupportedThresholds = [70, 75, 80];
    private readonly NitroSettings _settings;
    private readonly List<ProcessRowElements> _processRows = new(5);
    private MetricsSnapshot? _latestMetrics;
    private NativeMethods.RECT? _lastStripPhysicalRect;
    private QuietModeLevel _quietModeLevel;
    private bool _isBoostDisabled;
    private bool _isAwake;

    public ToolboxFlyoutWindow(NitroSettings settings)
    {
        _settings = settings;
        _settings.KeepAwakeMinutes = NormalizePresetMinutes(settings.KeepAwakeMinutes);
        _settings.AutoQuietThresholdCelsius = NormalizeAutoThreshold(settings.AutoQuietThresholdCelsius);
        _quietModeLevel = settings.QuietMode;
        InitializeComponent();
        InitializeProcessRows();
        VersionText.Text = $"v{UpdateService.CurrentVersionText}";
        StartupToggle.IsChecked = new StartupService().IsEnabled();
        AutomaticUpdatesToggle.IsChecked = settings.AutomaticallyCheckForUpdates;
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
        StatusText.Text = awake
            ? service.IsIndefinite
                ? "Awake · ∞"
                : $"Awake · {FormatCountdown(service.Remaining ?? TimeSpan.Zero)}"
            : "Idle";
        StatusText.Foreground = awake
            ? (Brush)FindResource("KaffeLabelAwakeBrush")
            : (Brush)FindResource("TextMutedBrush");
        StatusDot.Fill = awake
            ? (Brush)FindResource("KaffeAmberBrush")
            : (Brush)FindResource("TextMutedBrush");
        StatusChip.Background = awake
            ? (Brush)FindResource("KaffeGlowFillBrush")
            : (Brush)FindResource("CardBackgroundBrush");
        StatusChip.BorderBrush = awake
            ? (Brush)FindResource("KaffeGlowBorderBrush")
            : (Brush)FindResource("CardBorderBrush");

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
        _settings.QuietMode = mode;
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
        if (IsVisible)
        {
            SetSettingsViewVisible(false);
            Hide();
            return;
        }

        if ((DateTime.UtcNow - LastDeactivatedUtc).TotalMilliseconds < 180)
        {
            return;
        }

        SetSettingsViewVisible(false);
        _lastStripPhysicalRect = stripPhysicalRect;
        Show();
        UpdateLayout();
        PositionNearStrip(stripPhysicalRect);
        Activate();
    }

    private void PositionNearStrip(NativeMethods.RECT stripPhysicalRect)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var widthPhysical = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var heightPhysical = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var screenPoint = new System.Drawing.Point(stripPhysicalRect.Right, stripPhysicalRect.Top);
        var screen = WinForms.Screen.FromPoint(screenPoint);
        var marginPhysical = (int)Math.Round(8 * dpi.DpiScaleX);
        var x = stripPhysicalRect.Right - widthPhysical;
        var y = stripPhysicalRect.Top - heightPhysical - (int)Math.Round(10 * dpi.DpiScaleY);

        x = Math.Max(screen.WorkingArea.Left + marginPhysical,
            Math.Min(x, screen.WorkingArea.Right - widthPhysical - marginPhysical));
        y = Math.Max(screen.WorkingArea.Top + marginPhysical, y);

        Left = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        LastDeactivatedUtc = DateTime.UtcNow;
        SetSettingsViewVisible(false);
        Hide();
        base.OnDeactivated(e);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                SetSettingsViewVisible(false);
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
        SetSettingsViewVisible(SettingsPanel.Visibility != Visibility.Visible);
    }

    private void OnBackToMainClicked(object sender, RoutedEventArgs e)
    {
        SetSettingsViewVisible(false);
    }

    private void SetSettingsViewVisible(bool visible)
    {
        MainViewPanel.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        BrandHeader.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        SettingsHeader.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        StatusChip.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;

        SettingsButton.Background = visible
            ? (Brush)FindResource("RamFillBrush")
            : (Brush)FindResource("CardBackgroundBrush");
        SettingsButton.BorderBrush = visible
            ? (Brush)FindResource("RamBlueBrush")
            : (Brush)FindResource("CardBorderBrush");
        SettingsIconPath.Stroke = visible
            ? (Brush)FindResource("TextPrimaryBrush")
            : (Brush)FindResource("TextMutedBrush");

        if (IsVisible)
        {
            SizeToContent = SizeToContent.Height;
            InvalidateMeasure();
            UpdateLayout();
            if (_lastStripPhysicalRect is { } stripRect)
            {
                PositionNearStrip(stripRect);
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
        SetSelected(AutoTemp70Button, threshold == 70);
        SetSelected(AutoTemp75Button, threshold == 75);
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

    internal static int NormalizePresetMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return 0;
        }

        return SupportedPresets
            .Where(preset => preset > 0)
            .MinBy(preset => Math.Abs(preset - minutes));
    }

    internal static int NormalizeAutoThreshold(int thresholdCelsius) =>
        SupportedThresholds.MinBy(value => Math.Abs(value - thresholdCelsius));

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
