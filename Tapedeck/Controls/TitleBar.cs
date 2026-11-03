using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Tapedeck.Controls;

/// <summary>
/// Custom title bar for a window with an extended client area: brand mark, app name and pill-shaped window buttons.
/// The drag area and the buttons carry window decoration roles, so Windows treats them like native chrome (snap layouts).
/// </summary>
[TemplatePart(MinimizePartName, typeof(Button))]
[TemplatePart(MaximizePartName, typeof(Button))]
[TemplatePart(ClosePartName, typeof(Button))]
public sealed class TitleBar : TemplatedControl
{
    private const string MinimizePartName = "PART_Minimize";
    private const string MaximizePartName = "PART_Maximize";
    private const string ClosePartName = "PART_Close";

    private Button? _minimize;
    private Button? _maximize;
    private Button? _close;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Detach();

        _minimize = e.NameScope.Find<Button>(MinimizePartName);
        _maximize = e.NameScope.Find<Button>(MaximizePartName);
        _close = e.NameScope.Find<Button>(ClosePartName);

        _minimize?.Click += OnMinimize;
        _maximize?.Click += OnMaximize;
        _close?.Click += OnClose;
    }

    private void Detach()
    {
        _minimize?.Click -= OnMinimize;
        _maximize?.Click -= OnMaximize;
        _close?.Click -= OnClose;
    }

    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    private void OnMinimize(object? sender, RoutedEventArgs e) => Window?.WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (Window is { } window)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Window?.Close();
}
