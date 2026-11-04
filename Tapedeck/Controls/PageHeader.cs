using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Tapedeck.Controls;

/// <summary>
/// Page title with a summary line on the left and the page actions (<see cref="ContentControl.Content"/>) on the right.
/// Sub pages set <see cref="BackCommand"/> to get a round back button in front of the title.
/// </summary>
public sealed class PageHeader : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(Subtitle));

    public static readonly StyledProperty<ICommand?> BackCommandProperty =
        AvaloniaProperty.Register<PageHeader, ICommand?>(nameof(BackCommand));

    public ICommand? BackCommand
    {
        get => GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
