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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var values = CpuCalculator.DownsampleBars(Values, MaxBars);
        var gap = values.Length <= 14 ? 1.5 : 2.2;
        var barWidth = Math.Max(1.8, (ActualWidth - gap * (values.Length - 1)) / values.Length);

        for (var index = 0; index < values.Length; index++)
        {
            var percent = Math.Clamp(values[index] / 100.0, 0.18, 1.0);
            var height = ActualHeight * percent;
            var x = index * (barWidth + gap);
            drawingContext.DrawRoundedRectangle(
                BarBrush,
                null,
                new Rect(x, ActualHeight - height, barWidth, height),
                1.1,
                1.1);
        }
    }

    private static bool ValidateMaxBars(object value) => (int)value > 0;

    private static object CoerceMaxBars(DependencyObject _, object value) =>
        Math.Max(1, (int)value);
}
