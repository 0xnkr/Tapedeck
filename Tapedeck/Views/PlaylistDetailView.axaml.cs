using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Tapedeck.ViewModels;

namespace Tapedeck.Views;

/// <summary>
/// The playlist page. Reordering by drag and drop is view logic: the drag starts at a row's handle,
/// the drop target is the row under the pointer, and the view model does the move.
/// </summary>
public partial class PlaylistDetailView : UserControl
{
    // only drags started here are accepted; files from the Explorer are ignored
    private static readonly DataFormat<PlaylistEntryViewModel> ENTRY_FORMAT =
        DataFormat.CreateInProcessFormat<PlaylistEntryViewModel>("tapedeck-playlist-entry");

    public PlaylistDetailView()
    {
        this.InitializeComponent();
        this.EntryList.AddHandler(PointerPressedEvent, this.OnPointerPressed, RoutingStrategies.Tunnel);
        this.EntryList.AddHandler(DragDrop.DragOverEvent, this.OnDragOver);
        this.EntryList.AddHandler(DragDrop.DropEvent, this.OnDrop);
    }

    /// <summary>Back from the details: the track last shown there is scrolled into view.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.DataContext is PlaylistDetailViewModel { SelectedEntry: { } selected })
            Dispatcher.UIThread.Post(() => this.EntryList.ScrollIntoView(selected), DispatcherPriority.Loaded);
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control source
            || !e.GetCurrentPoint(source).Properties.IsLeftButtonPressed
            || !source.GetSelfAndVisualAncestors().OfType<Border>().Any(b => b.Classes.Contains("drag-handle"))
            || source.DataContext is not PlaylistEntryViewModel entry)
            return;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(ENTRY_FORMAT, entry));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(ENTRY_FORMAT) ? DragDropEffects.Move : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetValue(ENTRY_FORMAT) is not { } dragged
            || this.DataContext is not PlaylistDetailViewModel playlist)
            return;

        // dropped on a row: take its place; dropped below the last row: move to the end
        var target = (e.Source as Control)?.GetSelfAndVisualAncestors()
            .OfType<ListBoxItem>()
            .FirstOrDefault()?.DataContext as PlaylistEntryViewModel;
        var newIndex = target?.Position ?? playlist.Entries.Count - 1;
        await playlist.MoveAsync(dragged, newIndex);
    }
}
