using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Tapedeck.Resources;

namespace Tapedeck.Controls;

/// <summary>
/// Rating from 1 to 5 stars. Read-only it only displays the value (table),
/// otherwise each star is a button and clicking the current value clears it (detail page).
/// </summary>
[TemplatePart(STARS_PART_NAME, typeof(StackPanel))]
public sealed class RatingView : TemplatedControl
{
    public const int MAX_RATING = 5;
    private const string STARS_PART_NAME = "PART_Stars";

    public static readonly StyledProperty<int?> ValueProperty =
        AvaloniaProperty.Register<RatingView, int?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<RatingView, bool>(nameof(IsReadOnly));

    /// <summary>Edge length of one star icon.</summary>
    public static readonly StyledProperty<double> StarSizeProperty =
        AvaloniaProperty.Register<RatingView, double>(nameof(StarSize), 16);

    private StackPanel? stars;

    public int? Value
    {
        get => this.GetValue(ValueProperty);
        set => this.SetValue(ValueProperty, value);
    }

    public bool IsReadOnly
    {
        get => this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    public double StarSize
    {
        get => this.GetValue(StarSizeProperty);
        set => this.SetValue(StarSizeProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        this.stars = e.NameScope.Find<StackPanel>(STARS_PART_NAME);
        this.BuildStars();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsReadOnlyProperty || change.Property == StarSizeProperty)
            this.BuildStars();
        else if (change.Property == ValueProperty)
            this.UpdateStars();
    }

    private void BuildStars()
    {
        if (this.stars is null)
            return;

        this.stars.Children.Clear();
        var star = this.FindResource("Icon.Star") as Geometry;
        for (var rating = 1; rating <= MAX_RATING; rating++)
        {
            var glyph = new Glyph { Data = star, Width = StarSize, Height = StarSize, StrokeThickness = 1.6 };

            if (this.IsReadOnly)
            {
                this.stars.Children.Add(glyph);
                continue;
            }

            var button = new Button { Content = glyph, Tag = rating, Classes = { "star" } };
            AutomationProperties.SetName(button, string.Format(Strings.Rating_Value, rating, MAX_RATING));
            button.Click += this.OnStarClick;
            this.stars.Children.Add(button);
        }

        this.UpdateStars();
    }

    private void UpdateStars()
    {
        if (this.stars is null)
            return;

        var value = this.Value ?? 0;
        for (var i = 0; i < this.stars.Children.Count; i++)
        {
            var glyph = this.stars.Children[i] as Glyph ?? (Glyph)((Button)this.stars.Children[i]).Content!;
            glyph.Classes.Set("on", i < value);
            glyph.IsFilled = i < value;
        }

        AutomationProperties.SetName(this, value > 0 ? string.Format(Strings.Rating_Value, value, MAX_RATING) : Strings.Rating_None);
    }

    private void OnStarClick(object? sender, RoutedEventArgs e)
    {
        var rating = (int)((Button)sender!).Tag!;
        this.Value = this.Value == rating ? null : rating;
    }
}
