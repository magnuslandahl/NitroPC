using System.Windows;
using System.Windows.Media;
using WpfBrushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace NitroPC.Controls;

public sealed class RamSparklineControl : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(IReadOnlyList<double>),
            typeof(RamSparklineControl),
            new FrameworkPropertyMetadata(Array.Empty<double>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(
            nameof(StrokeBrush),
            typeof(Brush),
            typeof(RamSparklineControl),
            new FrameworkPropertyMetadata(WpfBrushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(
            nameof(FillBrush),
            typeof(Brush),
            typeof(RamSparklineControl),
            new FrameworkPropertyMetadata(WpfBrushes.SkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuideBrushProperty =
        DependencyProperty.Register(
            nameof(GuideBrush),
            typeof(Brush),
            typeof(RamSparklineControl),
            new FrameworkPropertyMetadata(WpfBrushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LatestPointBrushProperty =
        DependencyProperty.Register(
            nameof(LatestPointBrush),
            typeof(Brush),
            typeof(RamSparklineControl),
            new FrameworkPropertyMetadata(WpfBrushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxSamplesProperty =
        DependencyProperty.Register(
            nameof(MaxSamples),
            typeof(int),
            typeof(RamSparklineControl),
            new FrameworkPropertyMetadata(30, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceMaxSamples),
            ValidateMaxSamples);

    private static readonly Pen BaselinePen = CreateBaselinePen();

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush StrokeBrush
    {
        get => (Brush)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public Brush FillBrush
    {
        get => (Brush)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public Brush GuideBrush
    {
        get => (Brush)GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public Brush LatestPointBrush
    {
        get => (Brush)GetValue(LatestPointBrushProperty);
        set => SetValue(LatestPointBrushProperty, value);
    }

    public int MaxSamples
    {
        get => (int)GetValue(MaxSamplesProperty);
        set => SetValue(MaxSamplesProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var guidePen = new Pen(GuideBrush, 1)
        {
            DashStyle = new DashStyle([3, 3], 0)
        };
        drawingContext.DrawLine(guidePen, new Point(0, 2.5), new Point(ActualWidth, 2.5));
        drawingContext.DrawLine(
            BaselinePen,
            new Point(0, ActualHeight - 1),
            new Point(ActualWidth, ActualHeight - 1));

        if (Values.Count == 0)
        {
            return;
        }

        var count = Math.Min(Values.Count, MaxSamples);
        var sourceOffset = Values.Count - count;
        var firstSlot = MaxSamples - count;
        var (windowMin, windowMax) = ComputeDynamicWindow(Values, sourceOffset, count);
        var span = Math.Max(0.001, windowMax - windowMin);

        const double horizontalPad = 2.4;
        const double topPad = 4.5;
        const double bottomPad = 2.5;
        var usableWidth = Math.Max(1.0, ActualWidth - horizontalPad * 2);
        var plotHeight = Math.Max(1.0, ActualHeight - topPad - bottomPad);

        var points = new Point[count];
        for (var index = 0; index < count; index++)
        {
            var slot = firstSlot + index;
            var x = MaxSamples <= 1
                ? ActualWidth - horizontalPad
                : horizontalPad + slot * usableWidth / (MaxSamples - 1);
            var percent = Math.Clamp(Values[sourceOffset + index], 0, 100);
            var normalized = Math.Clamp((percent - windowMin) / span, 0.0, 1.0);
            var y = topPad + (1.0 - normalized) * plotHeight;
            points[index] = new Point(x, y);
        }

        if (points.Length >= 2)
        {
            var fillGeometry = new StreamGeometry();
            using (var context = fillGeometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                context.PolyLineTo(points.Skip(1).ToList(), true, false);
                context.LineTo(new Point(points[^1].X, ActualHeight - 1), true, false);
                context.LineTo(new Point(points[0].X, ActualHeight - 1), true, false);
            }
            fillGeometry.Freeze();
            drawingContext.DrawGeometry(FillBrush, null, fillGeometry);

            var lineGeometry = new StreamGeometry();
            using (var context = lineGeometry.Open())
            {
                context.BeginFigure(points[0], false, false);
                context.PolyLineTo(points.Skip(1).ToList(), true, false);
            }
            lineGeometry.Freeze();

            var linePen = new Pen(StrokeBrush, 1.8)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            drawingContext.DrawGeometry(null, linePen, lineGeometry);
        }
        else
        {
            drawingContext.DrawRoundedRectangle(
                FillBrush,
                null,
                new Rect(points[0].X - 1.2, points[0].Y, 2.4, Math.Max(1.0, ActualHeight - 1 - points[0].Y)),
                1.0,
                1.0);
        }

        var latest = points[^1];
        drawingContext.DrawEllipse(LatestPointBrush, null, latest, 2.2, 2.2);
    }

    internal static (double Min, double Max) ComputeDynamicWindow(
        IReadOnlyList<double> values,
        int offset,
        int count)
    {
        var min = 100.0;
        var max = 0.0;
        for (var index = 0; index < count; index++)
        {
            var sample = Math.Clamp(values[offset + index], 0.0, 100.0);
            if (sample < min)
            {
                min = sample;
            }

            if (sample > max)
            {
                max = sample;
            }
        }

        var mid = (min + max) / 2.0;
        var span = Math.Clamp(Math.Max(max - min, 2.0) * 1.25, 2.5, 100.0);
        var windowMin = mid - span / 2.0;
        var windowMax = mid + span / 2.0;

        if (windowMin < 0.0)
        {
            windowMax = Math.Min(100.0, windowMax - windowMin);
            windowMin = 0.0;
        }
        else if (windowMax > 100.0)
        {
            windowMin = Math.Max(0.0, windowMin - (windowMax - 100.0));
            windowMax = 100.0;
        }

        return (windowMin, windowMax);
    }

    private static Pen CreateBaselinePen()
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF));
        brush.Freeze();
        var pen = new Pen(brush, 1.0);
        pen.Freeze();
        return pen;
    }

    private static bool ValidateMaxSamples(object value) => (int)value > 0;

    private static object CoerceMaxSamples(DependencyObject _, object value) =>
        Math.Max(2, (int)value);
}
