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
[PseudoClasses(Done, Downloading, Pending, Error)]
public sealed class StatusChip : TemplatedControl
{
    private const string Done = ":done";
    private const string Downloading = ":downloading";
    private const string Pending = ":pending";
    private const string Error = ":error";

    public static readonly StyledProperty<DownloadStatus> StatusProperty =
        AvaloniaProperty.Register<StatusChip, DownloadStatus>(nameof(Status));

    /// <summary>Download progress from 0 to 1, only shown while downloading.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<StatusChip, double>(nameof(Progress));

    public StatusChip()
    {
        UpdatePseudoClasses();
    }

    public DownloadStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty)
            UpdatePseudoClasses();
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(Done, Status == DownloadStatus.Done);
        PseudoClasses.Set(Downloading, Status == DownloadStatus.Downloading);
        PseudoClasses.Set(Pending, Status == DownloadStatus.Pending);
        PseudoClasses.Set(Error, Status == DownloadStatus.Error);
    }
}
