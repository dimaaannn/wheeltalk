using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using WheelTalk.Core.Services;
using WheelTalk.Droid.Resources.Strings;
using Application = Android.App.Application;

namespace WheelTalk.Droid.Ble;

/// <summary>
/// Everything Android wants in place before Bluetooth will work. Checked before connecting rather
/// than once at startup, because permissions can be revoked and location switched off while the
/// app runs.
/// <para>
/// It used to say here that a wheel with a saved address needs none of this to reconnect. The
/// field test of 28.07.2026 disproved that: on Android 11 a connect to a known address fails the
/// same way a scan does, and the session then retries forever without ever saying why.
/// </para>
/// <para>
/// MAUI's <c>Permissions.RequestAsync&lt;T&gt;()</c> hid the current <see cref="Activity"/> behind
/// a global handler; the native replacement (<see cref="ActivityCompat"/>) needs it explicitly, so
/// every call here takes the activity asking (опись §1.2).
/// </para>
/// </summary>
public static class BleReadiness
{
    private const int RequestCode = 4200;

    /// <summary>
    /// One pending request at a time is all this app ever issues (nobody asks for permissions from
    /// two places at once), so a single static slot — filled by <see cref="RequestAsync"/>, drained
    /// by <see cref="OnRequestPermissionsResult"/> — is enough; no need to key it by request code.
    /// </summary>
    private static TaskCompletionSource<Permission[]>? _pending;

    /// <summary>Returns the typed cause and a message describing what stops scanning, or null when scanning can start.</summary>
    public static async Task<(LinkProblem Cause, string Message)?> FindProblemAsync(Activity activity)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // План 11 §2.3: POST_NOTIFICATIONS уже объявлено в манифесте, но раньше нигде не
            // запрашивалось в рантайме — без этого уведомление foreground-сервиса на Android 13+
            // не показывается вовсе. Запрашивается тут же, рядом с BLE-разрешениями, а не на
            // отдельном экране, чтобы не завести второе место, где про него можно забыть.
            await RequestAsync(activity, Manifest.Permission.PostNotifications!);
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var granted = await RequestAsync(activity,
                Manifest.Permission.BluetoothScan!, Manifest.Permission.BluetoothConnect!);
            return Array.TrueForAll(granted, p => p == Permission.Granted)
                ? null
                : (LinkProblem.NoPermissions, AppStrings.BleNoBluetoothPermission);
        }

        // Android 11 and older treat a BLE scan as a way to infer location, so both the permission
        // and the system location switch gate it. Without them startScan reports success and then
        // silently never calls back.
        var location = await RequestAsync(activity, Manifest.Permission.AccessFineLocation!);
        if (location[0] != Permission.Granted)
        {
            return (LinkProblem.NoPermissions, AppStrings.BleNoLocationPermission);
        }

        return IsLocationEnabled()
            ? null
            : (LinkProblem.BluetoothOff, AppStrings.BleLocationDisabled);
    }

    /// <summary>Already granted short-circuits without a system dialog; otherwise asks and awaits the result.</summary>
    private static Task<Permission[]> RequestAsync(Activity activity, params string[] permissions)
    {
        if (Array.TrueForAll(permissions, p => ContextCompat.CheckSelfPermission(activity, p) == Permission.Granted))
        {
            return Task.FromResult(Array.ConvertAll(permissions, _ => Permission.Granted));
        }

        _pending = new TaskCompletionSource<Permission[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        ActivityCompat.RequestPermissions(activity, permissions, RequestCode);
        return _pending.Task;
    }

    /// <summary>Forwarded from Activity.OnRequestPermissionsResult — that callback is the only way to learn the answer.</summary>
    public static void OnRequestPermissionsResult(int requestCode, Permission[] grantResults)
    {
        if (requestCode != RequestCode) return;
        _pending?.TrySetResult(grantResults);
    }

    private static bool IsLocationEnabled()
    {
        var manager = (LocationManager?)Application.Context.GetSystemService(Context.LocationService);
        return manager?.IsLocationEnabled ?? false;
    }
}
