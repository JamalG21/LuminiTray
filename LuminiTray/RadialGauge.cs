using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LuminiTray;

public class RadialGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RadialGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RadialGauge),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RadialGauge),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(RadialGauge),
            new FrameworkPropertyMetadata("MÉTRICA", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(RadialGauge),
            new FrameworkPropertyMetadata("ms", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(RadialGauge),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RadialGauge),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 20 || h <= 20) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double strokeThickness = Math.Clamp(Math.Min(w, h) * 0.075, 6, 12);
        double radius = (Math.Min(w, h) - strokeThickness * 2 - 16) / 2.0;
        if (radius <= 10) return;

        Point center = new(w / 2.0, h / 2.0 - 4);

        // Ângulo total de 240 graus: de 150° a 390° (ou -210° a +30°)
        const double startAngleDeg = 150.0;
        const double sweepAngleDeg = 240.0;

        // 1. Trilha de fundo
        var trackPen = new Pen(TrackBrush, strokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        trackPen.Freeze();

        var trackGeom = CreateArcGeometry(center, radius, startAngleDeg, sweepAngleDeg);
        dc.DrawGeometry(null, trackPen, trackGeom);

        // 2. Arco ativo de valor
        double max = Math.Max(Minimum + 0.001, Maximum);
        double frac = Math.Clamp((Value - Minimum) / (max - Minimum), 0.0, 1.0);

        if (frac > 0.002)
        {
            var valuePen = new Pen(AccentBrush, strokeThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            valuePen.Freeze();

            double activeSweep = sweepAngleDeg * frac;
            var activeGeom = CreateArcGeometry(center, radius, startAngleDeg, activeSweep);
            dc.DrawGeometry(null, valuePen, activeGeom);
        }

        // 3. Tipografia Central
        var typefaceTitle = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var typefaceValue = new Typeface(new FontFamily("Consolas, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var typefaceUnit = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        var titleBrush = new SolidColorBrush(Color.FromArgb(180, 160, 160, 160));
        var valueBrush = (Brush)Application.Current.TryFindResource("Fg") ?? Brushes.White;
        var unitBrush = AccentBrush;

        // Título Superior
        var formattedTitle = new FormattedText(
            Title.ToUpperInvariant(),
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typefaceTitle,
            Math.Clamp(radius * 0.22, 10, 12),
            titleBrush,
            dpi);

        dc.DrawText(formattedTitle, new Point(center.X - formattedTitle.Width / 2.0, center.Y - radius * 0.55));

        // Valor Numérico Central
        string valueStr = Value switch
        {
            >= 100 => $"{Value:0}",
            >= 10 => $"{Value:0.#}",
            > 0 => $"{Value:0.0}",
            _ => "0.0"
        };

        var formattedValue = new FormattedText(
            valueStr,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typefaceValue,
            Math.Clamp(radius * 0.44, 18, 28),
            valueBrush,
            dpi);

        dc.DrawText(formattedValue, new Point(center.X - formattedValue.Width / 2.0, center.Y - formattedValue.Height / 2.0));

        // Unidade Inferior
        var formattedUnit = new FormattedText(
            Unit,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typefaceUnit,
            Math.Clamp(radius * 0.22, 11, 13),
            unitBrush,
            dpi);

        dc.DrawText(formattedUnit, new Point(center.X - formattedUnit.Width / 2.0, center.Y + radius * 0.38));
    }

    private static PathGeometry CreateArcGeometry(Point center, double radius, double startAngleDeg, double sweepAngleDeg)
    {
        double endAngleDeg = startAngleDeg + sweepAngleDeg;
        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = endAngleDeg * Math.PI / 180.0;

        Point startPoint = new(
            center.X + radius * Math.Cos(startRad),
            center.Y + radius * Math.Sin(startRad));

        Point endPoint = new(
            center.X + radius * Math.Cos(endRad),
            center.Y + radius * Math.Sin(endRad));

        bool isLargeArc = sweepAngleDeg > 180.0;

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false
        };

        figure.Segments.Add(new ArcSegment(
            endPoint,
            new Size(radius, radius),
            0.0,
            isLargeArc,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }
}
