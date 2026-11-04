using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Tapedeck.ViewModels;

namespace Tapedeck.Views.Detail;

public partial class GenreEditor : UserControl
{
    public GenreEditor()
    {
        InitializeComponent();
        // The new genre field appears in place of the button; type right away
        NewGenreBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && NewGenreBox.IsVisible)
                NewGenreBox.Focus(NavigationMethod.Tab);
        };
        NewGenreBox.LostFocus += (_, _) =>
        {
            if (DataContext is TrackDetailViewModel detail && detail.IsAddingGenre)
                detail.AddGenreCommand.Execute(null);
        };
    }
}
