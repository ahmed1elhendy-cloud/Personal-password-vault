using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.Content;
using Android.OS;
#elif WINDOWS
using Windows.ApplicationModel.DataTransfer;
#endif

namespace PasswordVault.App;

public sealed class ClipboardService : IClipboardService
{
    public async Task CopySecret(string text, int clearAfterSeconds)
    {
        ArgumentNullException.ThrowIfNull(text);

#if ANDROID
        await MainThread.InvokeOnMainThreadAsync(() => SetAndroidClipboard(text));
#elif WINDOWS
        await MainThread.InvokeOnMainThreadAsync(() => SetWindowsClipboard(text));
#else
        await Clipboard.Default.SetTextAsync(text);
#endif

        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, clearAfterSeconds)));

#if ANDROID
        await MainThread.InvokeOnMainThreadAsync(() => ClearAndroidClipboardIfUnchanged(text));
#elif WINDOWS
        await MainThread.InvokeOnMainThreadAsync(() => ClearWindowsClipboardIfUnchangedAsync(text));
#else
        var currentText = await Clipboard.Default.GetTextAsync();
        if (string.Equals(currentText, text, StringComparison.Ordinal))
        {
            await Clipboard.Default.SetTextAsync(string.Empty);
        }
#endif
    }

#if ANDROID
    private static void SetAndroidClipboard(string text)
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("The Android activity is unavailable.");
        var clipboard = (Android.Content.ClipboardManager?)activity.GetSystemService(Context.ClipboardService)
            ?? throw new InvalidOperationException("The Android clipboard is unavailable.");
        var clip = ClipData.NewPlainText("Password", text)
            ?? throw new InvalidOperationException("The Android clipboard content could not be created.");

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var extras = new PersistableBundle();
            extras.PutBoolean(ClipDescription.ExtraIsSensitive, true);
            if (clip.Description is { } description)
            {
                description.Extras = extras;
            }
        }

        clipboard.PrimaryClip = clip;
    }

    private static void ClearAndroidClipboardIfUnchanged(string text)
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("The Android activity is unavailable.");
        var clipboard = (Android.Content.ClipboardManager?)activity.GetSystemService(Context.ClipboardService)
            ?? throw new InvalidOperationException("The Android clipboard is unavailable.");
        var currentText = clipboard.PrimaryClip?.GetItemAt(0)?.Text?.ToString();
        if (string.Equals(currentText, text, StringComparison.Ordinal))
        {
            clipboard.PrimaryClip = ClipData.NewPlainText(string.Empty, string.Empty);
        }
    }
#elif WINDOWS
    private static void SetWindowsClipboard(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        var options = new Windows.ApplicationModel.DataTransfer.ClipboardContentOptions
        {
            IsAllowedInHistory = false,
            IsRoamable = false
        };
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContentWithOptions(package, options);
    }

    private static async Task ClearWindowsClipboardIfUnchangedAsync(string text)
    {
        var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            var currentText = await content.GetTextAsync();
            if (string.Equals(currentText, text, StringComparison.Ordinal))
            {
                Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
            }
        }
    }
#endif
}
