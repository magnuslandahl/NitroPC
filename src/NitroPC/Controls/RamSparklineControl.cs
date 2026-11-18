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

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0 || Values.Count == 0)
        {
            return;
        }

        var guidePen = new Pen(GuideBrush, 1)
        {
            DashStyle = new DashStyle([3, 3], 0)
        };
        drawingContext.DrawLine(guidePen, new Point(0, 3), new Point(ActualWidth, 3));

        var points = new Point[Values.Count];
        for (var index = 0; index < Values.Count; index++)
        {
            var x = Values.Count == 1 ? 0 : index * ActualWidth / (Values.Count - 1);
            var percent = Math.Clamp(Values[index], 0, 100);
            var y = ActualHeight - 2 - percent / 100.0 * (ActualHeight - 5);
            points[index] = new Point(x, y);
        }

        var fillGeometry = new StreamGeometry();
        using (var context = fillGeometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.Skip(1).ToList(), true, false);
            context.LineTo(new Point(ActualWidth, ActualHeight), true, false);
            context.LineTo(new Point(0, ActualHeight), true, false);
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

        var linePen = new Pen(StrokeBrush, 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        drawingContext.DrawGeometry(null, linePen, lineGeometry);
        var latest = points[^1];
        drawingContext.DrawEllipse(LatestPointBrush, null, latest, 2.25, 2.25);
    }
}
