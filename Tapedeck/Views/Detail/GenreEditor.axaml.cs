using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Tapedeck.ViewModels;

namespace Tapedeck.Views.Detail;

public partial class GenreEditor : UserControl
{
    public GenreEditor()
    {
        this.InitializeComponent();
        // The new genre field appears in place of the button; type right away
        this.NewGenreBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && this.NewGenreBox.IsVisible)
                this.NewGenreBox.Focus(NavigationMethod.Tab);
        };
        this.NewGenreBox.LostFocus += (_, _) =>
        {
            if (this.DataContext is TrackDetailViewModel detail && detail.IsAddingGenre)
                detail.AddGenreCommand.Execute(null);
        };
    }
}
