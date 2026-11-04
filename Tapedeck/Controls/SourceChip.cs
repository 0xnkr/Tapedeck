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
        this.UpdatePseudoClasses();
    }

    public FieldSource Source
    {
        get => this.GetValue(SourceProperty);
        set => this.SetValue(SourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
            this.UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        this.PseudoClasses.Set(":none", this.Source == FieldSource.None);
        this.PseudoClasses.Set(":user", this.Source == FieldSource.User);
        this.PseudoClasses.Set(":mb", this.Source == FieldSource.Mb);
        this.PseudoClasses.Set(":lastfm", this.Source == FieldSource.Lastfm);
        this.PseudoClasses.Set(":yt", this.Source == FieldSource.Yt);
        this.PseudoClasses.Set(":parsed", this.Source == FieldSource.Parsed);
        this.PseudoClasses.Set(":channel", this.Source == FieldSource.Channel);
        this.PseudoClasses.Set(":derived", this.Source == FieldSource.Derived);
    }
}
