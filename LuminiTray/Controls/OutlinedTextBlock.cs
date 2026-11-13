using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LuminiTray.Controls;

/// <summary>
/// Controle vetorial de texto com contorno (stroke) sólido e nítido ao redor de cada glifo,
/// usando FormattedText + Geometry em vez de DropShadowEffect difuso.
/// </summary>
public class OutlinedTextBlock : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            SystemFonts.MessageFontSize,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            FontWeights.Normal,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            FontStyles.Normal,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty FontStretchProperty = TextElement.FontStretchProperty.AddOwner(
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            FontStretches.Normal,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            Brushes.Black,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            Brushes.Black,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            0.0,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextAlignmentProperty = DependencyProperty.Register(
        nameof(TextAlignment),
        typeof(TextAlignment),
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            TextAlignment.Left,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextTrimmingProperty = DependencyProperty.Register(
        nameof(TextTrimming),
        typeof(TextTrimming),
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            TextTrimming.None,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(
        nameof(TextWrapping),
        typeof(TextWrapping),
        typeof(OutlinedTextBlock),
        new FrameworkPropertyMetadata(
            TextWrapping.NoWrap,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontStretch FontStretch
    {
        get => (FontStretch)GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    private FormattedText CreateFormattedText(double maxWidth, double maxHeight)
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (dpi <= 0) dpi = 1.0;

        var ft = new FormattedText(
            Text ?? string.Empty,
            CultureInfo.CurrentUICulture,
            FlowDirection,
            typeface,
            Math.Max(1.0, FontSize),
            Foreground ?? Brushes.Black,
            dpi)
        {
            TextAlignment = TextAlignment,
            Trimming = TextTrimming
        };

        if (TextWrapping == TextWrapping.NoWrap)
        {
            ft.MaxLineCount = 1;
            if ((TextTrimming != TextTrimming.None || TextAlignment != TextAlignment.Left)
                && !double.IsInfinity(maxWidth) && maxWidth > 0)
            {
                ft.MaxTextWidth = maxWidth;
            }
        }
        else
        {
            ft.MaxLineCount = int.MaxValue;
            if (!double.IsInfinity(maxWidth) && maxWidth > 0)
                ft.MaxTextWidth = maxWidth;
        }

        if (!double.IsInfinity(maxHeight) && maxHeight > 0)
            ft.MaxTextHeight = maxHeight;

        return ft;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ft = CreateFormattedText(availableSize.Width, availableSize.Height);
        return new Size(Math.Ceiling(ft.Width), Math.Ceiling(ft.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (string.IsNullOrEmpty(Text))
            return;

        double w = RenderSize.Width;
        double h = RenderSize.Height;
        var ft = CreateFormattedText(w, h);

        double thickness = StrokeThickness;
        Brush? stroke = Stroke;
        bool hasStroke = thickness > 0.0 && stroke != null && (stroke is not SolidColorBrush scb || scb.Color.A > 0);

        if (!hasStroke)
        {
            drawingContext.DrawText(ft, new Point(0, 0));
            return;
        }

        var geometry = ft.BuildGeometry(new Point(0, 0));
        var pen = new Pen(stroke, thickness * 2.0)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        if (pen.CanFreeze && (stroke is not Freezable f || f.IsFrozen))
            pen.Freeze();

        drawingContext.DrawGeometry(null, pen, geometry);
        drawingContext.DrawGeometry(Foreground ?? Brushes.Black, null, geometry);
    }
}
