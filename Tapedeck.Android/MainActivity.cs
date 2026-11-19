using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Tapedeck.Android.Services;
using Tapedeck.ViewModels;

namespace Tapedeck.Android;

/// <summary>
/// The app's only Avalonia activity; started by <see cref="StorageAccessActivity"/> once the access is there.
/// Also takes links shared from other apps ("Share" → Tapedeck in the YouTube app) and adds them to the library.
/// </summary>
[Activity(
    Theme = "@style/Tapedeck.Theme",
    Icon = "@mipmap/icon",
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeType = "text/plain")]
public sealed class MainActivity : AvaloniaMainActivity
{
    private const int NOTIFICATION_REQUEST = 1;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // the notification of the work in the background; without it the work still runs, just unseen
        if (this.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            this.RequestPermissions([Manifest.Permission.PostNotifications], NOTIFICATION_REQUEST);
        // a recreated activity got its shared link already
        if (savedInstanceState is null)
            this.TakeSharedLink(this.Intent);
    }

    protected override void OnStart()
    {
        base.OnStart();
        // while the app is visible Android lets it start the work service; later it may be too late
        Notifier?.SetVisible(true);
    }

    protected override void OnStop()
    {
        Notifier?.SetVisible(false);
        base.OnStop();
    }

    private static WorkNotifier? Notifier => (Avalonia.Application.Current as App)?.Services?.GetService<WorkNotifier>();

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        this.TakeSharedLink(intent);
    }

    private void TakeSharedLink(Intent? intent)
    {
        if (intent?.Action != Intent.ActionSend || intent.GetStringExtra(Intent.ExtraText) is not { Length: > 0 } text)
            return;
        // handled once, also when the activity sees this intent again
        intent.SetAction(null);
        Dispatcher.UIThread.Post(() =>
            (Avalonia.Application.Current as App)?.Services?.GetService<MainViewModel>()?.AddSharedLink(text));
    }
}
