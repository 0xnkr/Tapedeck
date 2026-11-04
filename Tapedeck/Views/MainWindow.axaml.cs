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
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsXButton1Pressed || this.DataContext is not MainViewModel main)
            return;
        main.BackCommand.Execute(null);
        e.Handled = true;
    }
}
