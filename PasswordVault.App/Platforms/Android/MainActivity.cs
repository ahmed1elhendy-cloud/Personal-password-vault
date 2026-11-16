using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Auth.Api.Identity;
using Android.Gms.Common.Apis;
using Android.Gms.Extensions;
using Android.OS;
using Android.Views;
using Microsoft.Maui;

namespace PasswordVault.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    private const int GoogleAuthorizationRequestCode = 9137;
    private TaskCompletionSource<AuthorizationResult>? _authorizationResult;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.Secure);
    }

    public async Task<string> GetGoogleAccessTokenAsync(string[] scopes)
    {
        var client = Identity.GetAuthorizationClient(this);
        var request = AuthorizationRequest.InvokeBuilder()
            .SetRequestedScopes(scopes.Select(scope => new Scope(scope)).ToList())
            .Build();

        AuthorizationResult result;
        try
        {
            result = await client.Authorize(request).AsAsync<AuthorizationResult>();
            if (result.HasResolution)
            {
                var completion = new TaskCompletionSource<AuthorizationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _authorizationResult = completion;
                StartIntentSenderForResult(
                    result.PendingIntent!.IntentSender,
                    GoogleAuthorizationRequestCode,
                    null,
                    0,
                    0,
                    0);
                result = await completion.Task;
            }
        }
        catch (ApiException)
        {
            throw new GoogleAuthorizationException("Google authorization failed.");
        }
        finally
        {
            _authorizationResult = null;
        }

        if (result.AccessToken is not { Length: > 0 } accessToken
            || scopes.Any(scope => result.GrantedScopes?.Contains(scope) != true))
        {
            throw new GoogleAuthorizationException("Google access was not granted.");
        }

        return accessToken;
    }

    public async Task ClearGoogleAccessTokenAsync(string accessToken)
    {
        var request = ClearTokenRequest.InvokeBuilder()
            .SetToken(accessToken)
            .Build();
        try
        {
            await Identity.GetAuthorizationClient(this).ClearToken(request).AsAsync();
        }
        catch (ApiException)
        {
            throw new GoogleAuthorizationException("Google authorization expired.");
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != GoogleAuthorizationRequestCode || _authorizationResult is not { } completion)
        {
            return;
        }

        if (resultCode != Result.Ok || data is null)
        {
            completion.TrySetCanceled();
            return;
        }

        try
        {
            completion.TrySetResult(
                Identity.GetAuthorizationClient(this).GetAuthorizationResultFromIntent(data));
        }
        catch (ApiException)
        {
            completion.TrySetException(
                new GoogleAuthorizationException("Google authorization failed."));
        }
    }
}
