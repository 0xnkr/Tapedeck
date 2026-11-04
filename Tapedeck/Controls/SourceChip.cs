using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Tapedeck.Core.Models;

namespace Tapedeck.Controls;

/// <summary>
/// Small chip next to a metadata field that tells where its value comes from (user, YouTube, title, ...).
/// Label and colors per source live in the ControlTheme, keyed by the pseudo classes below.
/// </summary>
[PseudoClasses(":none", ":user", ":mb", ":lastfm", ":yt", ":parsed", ":channel", ":derived")]
public sealed class SourceChip : TemplatedControl
{
    public static readonly StyledProperty<FieldSource> SourceProperty =
        AvaloniaProperty.Register<SourceChip, FieldSource>(nameof(Source));

    public SourceChip()
    {
        UpdatePseudoClasses();
    }

    public FieldSource Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
            UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":none", Source == FieldSource.None);
        PseudoClasses.Set(":user", Source == FieldSource.User);
        PseudoClasses.Set(":mb", Source == FieldSource.Mb);
        PseudoClasses.Set(":lastfm", Source == FieldSource.Lastfm);
        PseudoClasses.Set(":yt", Source == FieldSource.Yt);
        PseudoClasses.Set(":parsed", Source == FieldSource.Parsed);
        PseudoClasses.Set(":channel", Source == FieldSource.Channel);
        PseudoClasses.Set(":derived", Source == FieldSource.Derived);
    }
}
