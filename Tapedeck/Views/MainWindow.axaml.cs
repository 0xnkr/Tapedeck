using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Tapedeck.ViewModels;

namespace Tapedeck.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        // macOS keeps its traffic lights only with the full decorations; they sit on top of the extended title bar
        if (System.OperatingSystem.IsMacOS())
            this.WindowDecorations = WindowDecorations.Full;
        // Tunnel so the mouse back button works wherever the pointer is
        this.AddHandler(PointerPressedEvent, this.OnPointerPressed, RoutingStrategies.Tunnel);
        this.AddHandler(KeyDownEvent, this.OnKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// Space pauses and resumes; on a track's details the left and right arrows go to the previous and next track.
    /// Not while a text field, a button or the slider has the key for itself.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None && !e.Handled
            && e.Source is not (TextBox or Slider)
            && this.DataContext is MainViewModel { Navigation.Current: TrackDetailViewModel detail })
        {
            var command = e.Key == Key.Left ? detail.PreviousCommand : detail.NextCommand;
            if (command.CanExecute(null))
                command.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space || e.KeyModifiers != KeyModifiers.None || e.Handled
            || e.Source is TextBox or Button or ToggleButton or Slider
            || this.DataContext is not MainViewModel { Player.HasTrack: true } main)
            return;
        main.TogglePlayback();
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsXButton1Pressed || this.DataContext is not MainViewModel main)
            return;
        main.BackCommand.Execute(null);
        e.Handled = true;
    }
}
