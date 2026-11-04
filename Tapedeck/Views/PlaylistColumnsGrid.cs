using Avalonia.Controls;

namespace Tapedeck.Views;

/// <summary>
/// The column layout of the playlist table, shared by the header and every row:
/// drag handle, number, thumbnail, title/artist, duration, status, remove.
/// </summary>
public sealed class PlaylistColumnsGrid : Grid
{
    public PlaylistColumnsGrid()
    {
        this.ColumnDefinitions = ColumnDefinitions.Parse("28,36,72,*,64,150,44");
        this.ColumnSpacing = 12;
    }
}
