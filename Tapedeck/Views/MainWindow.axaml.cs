using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Tapedeck.ViewModels;

namespace Tapedeck.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Tunnel so the mouse back button works wherever the pointer is
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsXButton1Pressed || DataContext is not MainViewModel main)
            return;
        main.BackCommand.Execute(null);
        e.Handled = true;
    }
}
