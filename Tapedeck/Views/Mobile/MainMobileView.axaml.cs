using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Tapedeck.ViewModels;

namespace Tapedeck.Views.Mobile;

/// <summary>
/// The phone's shell. Keeps its content clear of the status and navigation bars, which the app draws behind
/// (edge to edge), and answers the back gesture.
/// </summary>
public partial class MainMobileView : UserControl
{
    private TopLevel? topLevel;

    public MainMobileView()
    {
        this.InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        this.topLevel = TopLevel.GetTopLevel(this);
        if (this.topLevel is null)
            return;
        if (this.topLevel.InsetsManager is { } insets)
        {
            // drawn behind the bars always, so the padding is only ever this one
            insets.DisplayEdgeToEdgePreference = true;
            insets.SafeAreaChanged += this.OnSafeAreaChanged;
            this.ApplySafeArea(insets.SafeAreaPadding);
        }
        this.topLevel.BackRequested += this.OnBackRequested;
        this.topLevel.ScalingChanged += this.OnScalingChanged;
    }

    // on a cold start Avalonia reports the bars before the view knows its scaling, as pixels; it corrects them only
    // in SafeAreaPadding once the scaling is there, without raising SafeAreaChanged again
    private void OnScalingChanged(object? sender, System.EventArgs e)
    {
        if (this.topLevel?.InsetsManager is { } insets)
            this.ApplySafeArea(insets.SafeAreaPadding);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (this.topLevel is not null)
        {
            if (this.topLevel.InsetsManager is { } insets)
                insets.SafeAreaChanged -= this.OnSafeAreaChanged;
            this.topLevel.BackRequested -= this.OnBackRequested;
            this.topLevel.ScalingChanged -= this.OnScalingChanged;
        }
        this.topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnSafeAreaChanged(object? sender, SafeAreaChangedArgs e) => this.ApplySafeArea(e.SafeAreaPadding);

    /// <summary>Keeps clear of the bars only when the app really draws behind them; otherwise the system already does.</summary>
    private void ApplySafeArea(Thickness padding)
    {
        this.Padding = this.topLevel?.InsetsManager is { DisplaysEdgeToEdge: true } ? padding : default;
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is MainViewModel main && main.TryGoBack())
            e.Handled = true;
    }
}
