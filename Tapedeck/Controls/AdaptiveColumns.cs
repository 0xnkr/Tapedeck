using System;
using Avalonia;
using Avalonia.Controls;

namespace Tapedeck.Controls;

/// <summary>
/// Two columns side by side, or one below the other when there is not enough room (a phone).
/// The first column is <see cref="FirstWidth"/> wide, or half when that is not set; the second takes the rest.
/// </summary>
public sealed class AdaptiveColumns : Panel
{
    public static readonly StyledProperty<double> FirstWidthProperty =
        AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(FirstWidth), double.NaN);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(Spacing));

    /// <summary>Below this width the columns are stacked; the same as the pages' container queries.</summary>
    public static readonly StyledProperty<double> BreakWidthProperty =
        AvaloniaProperty.Register<AdaptiveColumns, double>(nameof(BreakWidth), 720);

    static AdaptiveColumns()
    {
        AffectsMeasure<AdaptiveColumns>(FirstWidthProperty, SpacingProperty, BreakWidthProperty);
    }

    public double FirstWidth
    {
        get => this.GetValue(FirstWidthProperty);
        set => this.SetValue(FirstWidthProperty, value);
    }

    public double Spacing
    {
        get => this.GetValue(SpacingProperty);
        set => this.SetValue(SpacingProperty, value);
    }

    public double BreakWidth
    {
        get => this.GetValue(BreakWidthProperty);
        set => this.SetValue(BreakWidthProperty, value);
    }

    private bool IsStacked(double width) => width < this.BreakWidth;

    private (double First, double Second) ColumnWidths(double width)
    {
        var first = double.IsNaN(this.FirstWidth) ? (width - this.Spacing) / 2 : Math.Min(this.FirstWidth, width);
        return (first, Math.Max(0, width - first - this.Spacing));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        if (double.IsInfinity(width) || this.IsStacked(width))
        {
            double height = 0, widest = 0;
            var visible = 0;
            foreach (var child in this.Children)
            {
                child.Measure(new Size(width, double.PositiveInfinity));
                if (!child.IsVisible)
                    continue;
                height += child.DesiredSize.Height + (visible++ > 0 ? this.Spacing : 0);
                widest = Math.Max(widest, child.DesiredSize.Width);
            }
            return new Size(double.IsInfinity(width) ? widest : width, height);
        }

        var (first, second) = this.ColumnWidths(width);
        double tallest = 0;
        for (var i = 0; i < this.Children.Count; i++)
        {
            var child = this.Children[i];
            child.Measure(new Size(i == 0 ? first : second, availableSize.Height));
            tallest = Math.Max(tallest, child.DesiredSize.Height);
        }
        return new Size(width, tallest);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (this.IsStacked(finalSize.Width))
        {
            double y = 0;
            foreach (var child in this.Children)
            {
                if (!child.IsVisible)
                    continue;
                child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
                y += child.DesiredSize.Height + this.Spacing;
            }
            return finalSize;
        }

        var (first, second) = this.ColumnWidths(finalSize.Width);
        for (var i = 0; i < this.Children.Count; i++)
        {
            var x = i == 0 ? 0 : first + this.Spacing;
            this.Children[i].Arrange(new Rect(x, 0, i == 0 ? first : second, finalSize.Height));
        }
        return finalSize;
    }
}
