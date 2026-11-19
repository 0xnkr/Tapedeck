using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Tapedeck.Android;

/// <summary>The app's only Avalonia activity; started by <see cref="StorageAccessActivity"/> once the access is there.</summary>
[Activity(
    Label = "Tapedeck",
    Theme = "@style/Tapedeck.Theme",
    Icon = "@mipmap/icon",
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
