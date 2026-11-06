using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Tapedeck.ViewModels;

namespace Tapedeck.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        // Tunnel so the mouse back button works wherever the pointer is
        this.AddHandler(PointerPressedEvent, this.OnPointerPressed, RoutingStrategies.Tunnel);
        this.AddHandler(KeyDownEvent, this.OnKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// On a track's details the left and right arrows go to the previous and next track,
    /// unless a text field has the key for itself.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right) || e.KeyModifiers != KeyModifiers.None || e.Handled
            || e.Source is TextBox or Slider
            || this.DataContext is not MainViewModel { Navigation.Current: TrackDetailViewModel detail })
            return;

        var command = e.Key == Key.Left ? detail.PreviousCommand : detail.NextCommand;
        if (command.CanExecute(null))
            command.Execute(null);
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
