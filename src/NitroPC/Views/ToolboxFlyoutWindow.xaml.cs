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
using WinForms = System.Windows.Forms;

namespace NitroPC.Views;

public sealed partial class ToolboxFlyoutWindow : Window
{
    private readonly NitroSettings _settings;

    public ToolboxFlyoutWindow(NitroSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        StartupToggle.IsChecked = new StartupService().IsEnabled();
        AutomaticUpdatesToggle.IsChecked = settings.AutomaticallyCheckForUpdates;
        CustomMinutesInput.Text = settings.KeepAwakeMinutes <= 0
            ? "60"
            : settings.KeepAwakeMinutes.ToString(CultureInfo.InvariantCulture);
        PreviewKeyDown += OnPreviewKeyDown;
        ApplySelectionStyles();
    }

    public DateTime LastDeactivatedUtc { get; private set; } = DateTime.MinValue;

    public event Action<NitroSettings>? SettingsChanged;

    public event Action<TimeSpan?>? KeepAwakeStartRequested;

    public event Action? KeepAwakeStopRequested;

    public event Action<bool>? StartupChangedRequested;

    public event Action? QuitRequested;

    public event Action? CheckForUpdatesRequested;

    public void UpdateMetrics(MetricsSnapshot metrics)
    {
        CpuLargeValue.Text = $"{Math.Round(metrics.CpuTotalPercent):0}%";
        CpuLargeBars.Values = _settings.CpuDisplayMode == CpuDisplayMode.History
            ? metrics.CpuHistoryPercents
            : metrics.CpuCorePercents;
        CpuLargeBars.HighlightLatestBar = _settings.CpuDisplayMode == CpuDisplayMode.History;
        CpuCoresText.Text = _settings.CpuDisplayMode == CpuDisplayMode.History
            ? "24-second history"
            : metrics.LogicalCoreCount > 24
                ? $"{metrics.LogicalCoreCount} logical cores, grouped"
                : $"{metrics.LogicalCoreCount} logical cores";
        RamLargeValue.Text = $"{Math.Round(metrics.RamPercent):0}%";
        RamLargeSparkline.Values = metrics.RamHistoryPercents;
        RamDetailText.Text = $"{metrics.RamUsedGb:0.0} / {metrics.RamTotalGb:0.0} GB used";
    }

    public void UpdateKeepAwake(KeepAwakeService service)
    {
        var awake = service.IsAwake;
        StatusText.Text = awake
            ? service.IsIndefinite
                ? "Awake · Indefinite (∞)"
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
            ? "Active — prevents sleep, lock & Teams away"
            : "Paused — normal Windows sleep & idle rules";
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
    }

    public void SetStartupEnabled(bool enabled)
    {
        if (StartupToggle.IsChecked == enabled)
        {
            return;
        }

        StartupToggle.IsChecked = enabled;
    }

    public void SetUpdateStatus(string text)
    {
        UpdateStatusText.Text = text;
    }

    internal void ToggleNearStrip(NativeMethods.RECT stripPhysicalRect)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if ((DateTime.UtcNow - LastDeactivatedUtc).TotalMilliseconds < 180)
        {
            return;
        }

        Show();
        UpdateLayout();

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
        Activate();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        LastDeactivatedUtc = DateTime.UtcNow;
        Hide();
        base.OnDeactivated(e);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
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

        if (TryReadCustomMinutes(out var minutes))
        {
            _settings.KeepAwakeMinutes = minutes;
            SettingsChanged?.Invoke(_settings);
            KeepAwakeStartRequested?.Invoke(TimeSpan.FromMinutes(minutes));
        }
    }

    private void OnPresetClicked(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var minutes = int.Parse((string)button.Tag, CultureInfo.InvariantCulture);
        _settings.KeepAwakeMinutes = minutes;
        CustomMinutesInput.Text = minutes == 0 ? string.Empty : minutes.ToString(CultureInfo.InvariantCulture);
        SettingsChanged?.Invoke(_settings);
        KeepAwakeStartRequested?.Invoke(minutes == 0 ? null : TimeSpan.FromMinutes(minutes));
        ApplySelectionStyles();
    }

    private void OnCustomMinutesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !TryReadCustomMinutes(out var minutes))
        {
            return;
        }

        _settings.KeepAwakeMinutes = minutes;
        SettingsChanged?.Invoke(_settings);
        KeepAwakeStartRequested?.Invoke(TimeSpan.FromMinutes(minutes));
        ApplySelectionStyles();
        e.Handled = true;
    }

    private void OnRamModeClicked(object sender, RoutedEventArgs e)
    {
        var mode = Enum.Parse<RamDisplayMode>((string)((Button)sender).Tag);
        _settings.RamDisplayMode = mode;
        SettingsChanged?.Invoke(_settings);
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

    private bool TryReadCustomMinutes(out int minutes)
    {
        var parsed = int.TryParse(
            CustomMinutesInput.Text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out minutes);
        if (parsed && minutes is >= 1 and <= 1440)
        {
            return true;
        }

        CustomMinutesInput.Text = _settings.KeepAwakeMinutes <= 0
            ? "60"
            : _settings.KeepAwakeMinutes.ToString(CultureInfo.InvariantCulture);
        SetUpdateStatus("Custom duration must be between 1 and 1440 minutes.");
        return false;
    }

    private void ApplySelectionStyles()
    {
        SetSelected(RamPercentButton, _settings.RamDisplayMode == RamDisplayMode.Percent);
        SetSelected(RamGigabytesButton, _settings.RamDisplayMode == RamDisplayMode.Gigabytes);
        SetSelected(RamBothButton, _settings.RamDisplayMode == RamDisplayMode.Both);
        SetSelected(BarBadgeButton, _settings.KeepAwakeBarMode == KeepAwakeBarMode.Badge);
        SetSelected(BarIconButton, _settings.KeepAwakeBarMode == KeepAwakeBarMode.CompactIcon);
        SetSelected(BarHiddenButton, _settings.KeepAwakeBarMode == KeepAwakeBarMode.Hidden);
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

    private static string FormatCountdown(TimeSpan remaining) =>
        $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
}
