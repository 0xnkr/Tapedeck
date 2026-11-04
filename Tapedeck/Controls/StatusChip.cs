using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Tapedeck.Core.Models;

namespace Tapedeck.Controls;

/// <summary>
/// Download status of a track: icon, word and color pair. While downloading it shows a label with a progress bar instead.
/// The look per status lives in the ControlTheme, keyed by the pseudo classes below.
/// </summary>
[PseudoClasses(DONE, DOWNLOADING, PENDING, ERROR)]
public sealed class StatusChip : TemplatedControl
{
    private const string DONE = ":done";
    private const string DOWNLOADING = ":downloading";
    private const string PENDING = ":pending";
    private const string ERROR = ":error";

    public static readonly StyledProperty<DownloadStatus> StatusProperty =
        AvaloniaProperty.Register<StatusChip, DownloadStatus>(nameof(Status));

    /// <summary>Download progress from 0 to 1, only shown while downloading.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<StatusChip, double>(nameof(Progress));

    public StatusChip()
    {
        this.UpdatePseudoClasses();
    }

    public DownloadStatus Status
    {
        get => this.GetValue(StatusProperty);
        set => this.SetValue(StatusProperty, value);
    }

    public double Progress
    {
        get => this.GetValue(ProgressProperty);
        set => this.SetValue(ProgressProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty)
            this.UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        this.PseudoClasses.Set(DONE, this.Status == DownloadStatus.Done);
        this.PseudoClasses.Set(DOWNLOADING, this.Status == DownloadStatus.Downloading);
        this.PseudoClasses.Set(PENDING, this.Status == DownloadStatus.Pending);
        this.PseudoClasses.Set(ERROR, this.Status == DownloadStatus.Error);
    }
}
