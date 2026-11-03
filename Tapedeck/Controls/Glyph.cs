using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Tapedeck.Controls;

/// <summary>
/// Draws an icon geometry from a 24 unit grid, scaled to the control's size.
/// Strokes in the inherited <see cref="Foreground"/> (like <c>currentColor</c> in SVG) with round caps and joins.
/// </summary>
public sealed class Glyph : Control
{
    private const double GridSize = 24;
    private const double DefaultSize = 20;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Glyph, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Glyph>();

    /// <summary>Stroke width in grid units. 0 draws no stroke.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Glyph, double>(nameof(StrokeThickness), 1.8);

    /// <summary>Fills the geometry with <see cref="Foreground"/> in addition to the stroke.</summary>
    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<Glyph, bool>(nameof(IsFilled));

    static Glyph()
    {
        AffectsRender<Glyph>(DataProperty, ForegroundProperty, StrokeThicknessProperty, IsFilledProperty);
        AffectsMeasure<Glyph>(DataProperty);
    }

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(DefaultSize, DefaultSize);

    public override void Render(DrawingContext context)
    {
        if (Data is null || Foreground is null)
            return;

        var size = Math.Min(Bounds.Width, Bounds.Height);
        var scale = size / GridSize;
        var offset = new Point((Bounds.Width - size) / 2, (Bounds.Height - size) / 2);

        var pen = StrokeThickness > 0
            ? new Pen(Foreground, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
            : null;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
            context.DrawGeometry(IsFilled ? Foreground : null, pen, Data);
    }
}
