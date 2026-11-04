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
    private const double GRID_SIZE = 24;
    private const double DEFAULT_SIZE = 20;

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
        get => this.GetValue(DataProperty);
        set => this.SetValue(DataProperty, value);
    }

    public IBrush? Foreground
    {
        get => this.GetValue(ForegroundProperty);
        set => this.SetValue(ForegroundProperty, value);
    }

    public double StrokeThickness
    {
        get => this.GetValue(StrokeThicknessProperty);
        set => this.SetValue(StrokeThicknessProperty, value);
    }

    public bool IsFilled
    {
        get => this.GetValue(IsFilledProperty);
        set => this.SetValue(IsFilledProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(DEFAULT_SIZE, DEFAULT_SIZE);

    public override void Render(DrawingContext context)
    {
        if (this.Data is null || this.Foreground is null)
            return;

        var size = Math.Min(this.Bounds.Width, this.Bounds.Height);
        var scale = size / GRID_SIZE;
        var offset = new Point((this.Bounds.Width - size) / 2, (this.Bounds.Height - size) / 2);

        var pen = this.StrokeThickness > 0
            ? new Pen(this.Foreground, this.StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
            : null;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
            context.DrawGeometry(this.IsFilled ? this.Foreground : null, pen, this.Data);
    }
}
