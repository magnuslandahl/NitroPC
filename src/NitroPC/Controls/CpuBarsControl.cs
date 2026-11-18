using System.Windows;
using System.Windows.Media;
using NitroPC.Helpers;
using WpfBrushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;

namespace NitroPC.Controls;

public sealed class CpuBarsControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(IReadOnlyList<double>),
            typeof(CpuBarsControl),
            new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxBarsProperty =
        DependencyProperty.Register(
            nameof(MaxBars),
            typeof(int),
            typeof(CpuBarsControl),
            new FrameworkPropertyMetadata(14, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceMaxBars),
            ValidateMaxBars);

    public static readonly DependencyProperty BarBrushProperty =
        DependencyProperty.Register(
            nameof(BarBrush),
            typeof(Brush),
            typeof(CpuBarsControl),
            new FrameworkPropertyMetadata(WpfBrushes.Coral, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LatestBarBrushProperty =
        DependencyProperty.Register(
            nameof(LatestBarBrush),
            typeof(Brush),
            typeof(CpuBarsControl),
            new FrameworkPropertyMetadata(WpfBrushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightLatestBarProperty =
        DependencyProperty.Register(
            nameof(HighlightLatestBar),
            typeof(bool),
            typeof(CpuBarsControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsHistoryModeProperty =
        DependencyProperty.Register(
            nameof(IsHistoryMode),
            typeof(bool),
            typeof(CpuBarsControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush EmptySlotBrush = CreateFrozenBrush(0x16, 0xFF, 0xFF, 0xFF);

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public int MaxBars
    {
        get => (int)GetValue(MaxBarsProperty);
        set => SetValue(MaxBarsProperty, value);
    }

    public Brush BarBrush
    {
        get => (Brush)GetValue(BarBrushProperty);
        set => SetValue(BarBrushProperty, value);
    }

    public Brush LatestBarBrush
    {
        get => (Brush)GetValue(LatestBarBrushProperty);
        set => SetValue(LatestBarBrushProperty, value);
    }

    public bool HighlightLatestBar
    {
        get => (bool)GetValue(HighlightLatestBarProperty);
        set => SetValue(HighlightLatestBarProperty, value);
    }

    public bool IsHistoryMode
    {
        get => (bool)GetValue(IsHistoryModeProperty);
        set => SetValue(IsHistoryModeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        if (IsHistoryMode)
        {
            RenderHistoryBars(drawingContext);
            return;
        }

        var values = CpuCalculator.DownsampleBars(Values, MaxBars);
        var (barWidth, gap) = ComputeSlotMetrics(values.Length);

        for (var index = 0; index < values.Length; index++)
        {
            var percent = Math.Clamp(values[index] / 100.0, 0.14, 1.0);
            var height = ActualHeight * percent;
            var x = index * (barWidth + gap);
            drawingContext.DrawRoundedRectangle(
                HighlightLatestBar && index == values.Length - 1 ? LatestBarBrush : BarBrush,
                null,
                new Rect(x, ActualHeight - height, barWidth, height),
                1.0,
                1.0);
        }
    }

    private void RenderHistoryBars(DrawingContext drawingContext)
    {
        var slotCount = MaxBars;
        var (barWidth, gap) = ComputeSlotMetrics(slotCount);
        var count = Math.Min(Values.Count, slotCount);
        var sourceOffset = Values.Count - count;
        var firstActiveSlot = slotCount - count;

        for (var slot = 0; slot < firstActiveSlot; slot++)
        {
            var emptyX = slot * (barWidth + gap);
            drawingContext.DrawRoundedRectangle(
                EmptySlotBrush,
                null,
                new Rect(emptyX, ActualHeight - 2.0, barWidth, 2.0),
                0.8,
                0.8);
        }

        for (var index = 0; index < count; index++)
        {
            var slot = firstActiveSlot + index;
            var percent = Math.Clamp(Values[sourceOffset + index] / 100.0, 0.14, 1.0);
            var height = ActualHeight * percent;
            var x = slot * (barWidth + gap);
            var brush = HighlightLatestBar && index == count - 1 ? LatestBarBrush : BarBrush;
            drawingContext.DrawRoundedRectangle(
                brush,
                null,
                new Rect(x, ActualHeight - height, barWidth, height),
                1.0,
                1.0);
        }
    }

    private (double BarWidth, double Gap) ComputeSlotMetrics(int slots)
    {
        var gap = ActualWidth <= 60
            ? 0.85
            : slots >= 24
                ? 1.4
                : 1.8;
        var barWidth = Math.Max(0.9, (ActualWidth - gap * Math.Max(0, slots - 1)) / Math.Max(1, slots));
        return (barWidth, gap);
    }

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static bool ValidateMaxBars(object value) => (int)value > 0;

    private static object CoerceMaxBars(DependencyObject _, object value) =>
        Math.Max(1, (int)value);
}
