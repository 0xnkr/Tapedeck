using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Tapedeck.Controls;

/// <summary>
/// Custom title bar for a window with an extended client area: brand mark, app name and pill-shaped window buttons.
/// The drag area and the buttons carry window decoration roles, so Windows treats them like native chrome (snap layouts).
/// </summary>
[TemplatePart(MINIMIZE_PART_NAME, typeof(Button))]
[TemplatePart(MAXIMIZE_PART_NAME, typeof(Button))]
[TemplatePart(CLOSE_PART_NAME, typeof(Button))]
public sealed class TitleBar : TemplatedControl
{
    private const string MINIMIZE_PART_NAME = "PART_Minimize";
    private const string MAXIMIZE_PART_NAME = "PART_Maximize";
    private const string CLOSE_PART_NAME = "PART_Close";

    private Button? minimize;
    private Button? maximize;
    private Button? close;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        this.Detach();

        this.minimize = e.NameScope.Find<Button>(MINIMIZE_PART_NAME);
        this.maximize = e.NameScope.Find<Button>(MAXIMIZE_PART_NAME);
        this.close = e.NameScope.Find<Button>(CLOSE_PART_NAME);

        minimize?.Click += this.OnMinimize;
        maximize?.Click += this.OnMaximize;
        close?.Click += this.OnClose;
    }

    private void Detach()
    {
        minimize?.Click -= this.OnMinimize;
        maximize?.Click -= this.OnMaximize;
        close?.Click -= this.OnClose;
    }

    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    private void OnMinimize(object? sender, RoutedEventArgs e) => Window?.WindowState = WindowState.Minimized;

    private void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (this.Window is { } window)
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Window?.Close();
}
