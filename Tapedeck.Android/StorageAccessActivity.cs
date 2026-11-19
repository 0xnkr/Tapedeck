using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using Tapedeck.Resources;
using AndroidEnvironment = Android.OS.Environment;
using AndroidSettings = Android.Provider.Settings;
using AndroidUri = Android.Net.Uri;

namespace Tapedeck.Android;

/// <summary>
/// The launcher: goes straight on to <see cref="MainActivity"/> when the access to all files is there, otherwise
/// explains why Tapedeck needs it and opens the system screen to allow it. Without the access the library folder
/// (Music/Tapedeck) cannot be written.
/// </summary>
[Activity(
    Label = "Tapedeck",
    Theme = "@style/Tapedeck.Theme",
    Icon = "@mipmap/icon",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class StorageAccessActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (AndroidEnvironment.IsExternalStorageManager)
        {
            this.OpenApp();
            return;
        }

        var padding = this.Dp(24);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetGravity(GravityFlags.CenterVertical);
        layout.SetPadding(padding, padding, padding, padding);

        var title = new TextView(this) { Text = Strings.StorageAccess_Title, TextSize = 22 };
        var text = new TextView(this) { Text = Strings.StorageAccess_Text, TextSize = 16 };
        text.SetPadding(0, this.Dp(12), 0, this.Dp(24));
        var grant = new Button(this) { Text = Strings.StorageAccess_Grant };
        grant.Click += (_, _) => this.StartActivity(new Intent(
            AndroidSettings.ActionManageAppAllFilesAccessPermission, AndroidUri.Parse($"package:{this.PackageName}")));

        layout.AddView(title);
        layout.AddView(text);
        layout.AddView(grant);
        this.SetContentView(layout);
    }

    protected override void OnResume()
    {
        base.OnResume();
        // back from the system screen
        if (AndroidEnvironment.IsExternalStorageManager)
            this.OpenApp();
    }

    private void OpenApp()
    {
        this.StartActivity(new Intent(this, typeof(MainActivity)));
        this.Finish();
    }

    private int Dp(int value) => (int)(value * (this.Resources?.DisplayMetrics?.Density ?? 1));
}
