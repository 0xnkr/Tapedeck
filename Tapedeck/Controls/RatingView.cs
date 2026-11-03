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
[TemplatePart(StarsPartName, typeof(StackPanel))]
public sealed class RatingView : TemplatedControl
{
    public const int MaxRating = 5;
    private const string StarsPartName = "PART_Stars";

    public static readonly StyledProperty<int?> ValueProperty =
        AvaloniaProperty.Register<RatingView, int?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<RatingView, bool>(nameof(IsReadOnly));

    /// <summary>Edge length of one star icon.</summary>
    public static readonly StyledProperty<double> StarSizeProperty =
        AvaloniaProperty.Register<RatingView, double>(nameof(StarSize), 16);

    private StackPanel? _stars;

    public int? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public double StarSize
    {
        get => GetValue(StarSizeProperty);
        set => SetValue(StarSizeProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _stars = e.NameScope.Find<StackPanel>(StarsPartName);
        BuildStars();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsReadOnlyProperty || change.Property == StarSizeProperty)
            BuildStars();
        else if (change.Property == ValueProperty)
            UpdateStars();
    }

    private void BuildStars()
    {
        if (_stars is null)
            return;

        _stars.Children.Clear();
        var star = this.FindResource("Icon.Star") as Geometry;
        for (var rating = 1; rating <= MaxRating; rating++)
        {
            var glyph = new Glyph { Data = star, Width = StarSize, Height = StarSize, StrokeThickness = 1.6 };

            if (IsReadOnly)
            {
                _stars.Children.Add(glyph);
                continue;
            }

            var button = new Button { Content = glyph, Tag = rating, Classes = { "star" } };
            AutomationProperties.SetName(button, string.Format(Strings.Rating_Value, rating, MaxRating));
            button.Click += OnStarClick;
            _stars.Children.Add(button);
        }

        UpdateStars();
    }

    private void UpdateStars()
    {
        if (_stars is null)
            return;

        var value = Value ?? 0;
        for (var i = 0; i < _stars.Children.Count; i++)
        {
            var glyph = _stars.Children[i] as Glyph ?? (Glyph)((Button)_stars.Children[i]).Content!;
            glyph.Classes.Set("on", i < value);
            glyph.IsFilled = i < value;
        }

        AutomationProperties.SetName(this, value > 0 ? string.Format(Strings.Rating_Value, value, MaxRating) : Strings.Rating_None);
    }

    private void OnStarClick(object? sender, RoutedEventArgs e)
    {
        var rating = (int)((Button)sender!).Tag!;
        Value = Value == rating ? null : rating;
    }
}
