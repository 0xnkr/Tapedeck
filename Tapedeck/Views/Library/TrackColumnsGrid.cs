using Avalonia.Controls;

namespace Tapedeck.Views.Library;

/// <summary>
/// The column layout of the track table, shared by the header and every row:
/// number, thumbnail, title/artist, album, duration, rating, status, more.
/// </summary>
public sealed class TrackColumnsGrid : Grid
{
    public TrackColumnsGrid()
    {
        ColumnDefinitions = ColumnDefinitions.Parse("36,72,2.2*,1.4*,64,116,150,44");
        ColumnSpacing = 12;
    }
}
