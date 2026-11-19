using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Wifi;
using Android.OS;
using Tapedeck.Resources;

namespace Tapedeck.Android.Services;

/// <summary>
/// Keeps the app running while it works (downloads, imports, background queues), also with the screen off:
/// a foreground service with a notification that shows the same text as the activity toast, plus a wake lock and a
/// Wi-Fi lock, because a foreground service alone does not keep the CPU and the radio awake.
/// Started and stopped by <see cref="WorkNotifier"/>.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class WorkService : Service
{
    public const string EXTRA_TEXT = "text";
    private const string CHANNEL = "work";
    private const int NOTIFICATION = 1;

    private PowerManager.WakeLock? wakeLock;
    private WifiManager.WifiLock? wifiLock;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        this.StartForeground(NOTIFICATION, Build(this, intent?.GetStringExtra(EXTRA_TEXT)), ForegroundService.TypeDataSync);
        if (this.wakeLock is null && this.GetSystemService(PowerService) is PowerManager power)
        {
            this.wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "Tapedeck:work");
            this.wakeLock?.Acquire();
        }
        if (this.wifiLock is null && this.GetSystemService(WifiService) is WifiManager wifi)
        {
            this.wifiLock = wifi.CreateWifiLock(global::Android.Net.WifiMode.FullLowLatency, "Tapedeck:work");
            this.wifiLock?.Acquire();
        }
        // after the system killed the app, nothing is left to work on
        return StartCommandResult.NotSticky;
    }

    public override void OnDestroy()
    {
        if (this.wakeLock is { IsHeld: true })
            this.wakeLock.Release();
        if (this.wifiLock is { IsHeld: true })
            this.wifiLock.Release();
        this.wakeLock = null;
        this.wifiLock = null;
        base.OnDestroy();
    }

    /// <summary>Shows a new text in the running notification.</summary>
    public static void Update(Context context, string text)
    {
        if (context.GetSystemService(NotificationService) is NotificationManager manager)
            manager.Notify(NOTIFICATION, Build(context, text));
    }

    private static Notification Build(Context context, string? text)
    {
        var manager = (NotificationManager)context.GetSystemService(NotificationService)!;
        if (manager.GetNotificationChannel(CHANNEL) is null)
            manager.CreateNotificationChannel(new NotificationChannel(CHANNEL, Strings.Notification_WorkChannel, NotificationImportance.Low));

        // a tap brings the app back
        var open = PendingIntent.GetActivity(context, 0,
            new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        return new Notification.Builder(context, CHANNEL)
            .SetContentTitle("Tapedeck")!
            .SetContentText(text)!
            .SetSmallIcon(global::Android.Resource.Drawable.StatSysDownload)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetContentIntent(open)!
            .Build()!;
    }
}
