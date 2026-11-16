using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using PasswordVault.Core;

#if WINDOWS
using System.Net;
using System.Net.Sockets;
#endif

namespace PasswordVault.App;

internal static class GoogleScopes
{
    internal const string DriveAppData = "https://www.googleapis.com/auth/drive.appdata";
}

internal sealed class GoogleAuthorizationException : Exception
{
    internal GoogleAuthorizationException(string message) : base(message) { }
    internal GoogleAuthorizationException(string message, Exception innerException) : base(message, innerException) { }
}

internal sealed class GoogleDriveException : Exception
{
    internal GoogleDriveException(string message) : base(message) { }
    internal GoogleDriveException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class GoogleAuthService
{
#if WINDOWS
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RefreshTokenPrefix = "google.refresh.";
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CachedToken> _accessTokens = new(StringComparer.Ordinal);

    public GoogleAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }
#else
    private readonly SemaphoreSlim _androidGate = new(1, 1);
    public GoogleAuthService() { }
#endif

    public async Task<string> GetAccessTokenAsync(string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        if (scopes.Length == 0 || scopes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one valid OAuth scope is required.", nameof(scopes));
        }

        var normalizedScopes = scopes.Distinct(StringComparer.Ordinal)
            .OrderBy(scope => scope, StringComparer.Ordinal)
            .ToArray();
#if ANDROID
        var activity = Platform.CurrentActivity as MainActivity
            ?? throw new GoogleAuthorizationException("Open the app to connect to Google.");
        await _androidGate.WaitAsync();
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(
                () => activity.GetGoogleAccessTokenAsync(normalizedScopes));
        }
        catch (GoogleAuthorizationException)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new GoogleAuthorizationException("Google sign-in was canceled.", ex);
        }
        finally
        {
            _androidGate.Release();
        }
#elif WINDOWS
        try
        {
            return await GetWindowsAccessTokenAsync(normalizedScopes);
        }
        catch (GoogleAuthorizationException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleAuthorizationException("Couldn't reach Google.", ex);
        }
        catch (SocketException ex)
        {
            throw new GoogleAuthorizationException("Couldn't start Google sign-in.", ex);
        }
        catch (HttpListenerException ex)
        {
            throw new GoogleAuthorizationException("Couldn't start Google sign-in.", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new GoogleAuthorizationException("Google sign-in timed out.", ex);
        }
#else
        throw new PlatformNotSupportedException("Google authorization is not configured for this platform.");
#endif
    }

#if WINDOWS
    private async Task<string> GetWindowsAccessTokenAsync(string[] scopes)
    {
        if (string.IsNullOrWhiteSpace(GoogleConfig.DesktopClientId))
        {
            throw new GoogleAuthorizationException("Add the Desktop OAuth client ID to GoogleConfig.cs.");
        }

        var cacheKey = GetScopeKey(scopes);
        await _gate.WaitAsync();
        try
        {
            if (_accessTokens.TryGetValue(cacheKey, out var cached)
                && cached.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return cached.AccessToken;
            }

            var refreshKey = RefreshTokenPrefix + cacheKey;
            var refreshToken = await SecureStorage.Default.GetAsync(refreshKey);
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var refreshed = await TryRefreshAsync(refreshToken);
                if (refreshed is not null)
                {
                    if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
                    {
                        await SecureStorage.Default.SetAsync(refreshKey, refreshed.RefreshToken);
                    }

                    _accessTokens[cacheKey] = refreshed;
                    return refreshed.AccessToken;
                }

                SecureStorage.Default.Remove(refreshKey);
            }

            var authorized = await AuthorizeWindowsAsync(scopes);
            if (!string.IsNullOrWhiteSpace(authorized.RefreshToken))
            {
                await SecureStorage.Default.SetAsync(refreshKey, authorized.RefreshToken);
            }

            _accessTokens[cacheKey] = authorized;
            return authorized.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CachedToken?> TryRefreshAsync(string refreshToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = GoogleConfig.DesktopClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        using var response = await _httpClient.PostAsync(TokenEndpoint, content);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await ReadTokenResponseAsync(response, refreshToken);
    }

    private async Task<CachedToken> AuthorizeWindowsAsync(string[] scopes)
    {
        using var tcpListener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((System.Net.IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();

        var redirectUri = $"http://127.0.0.1:{port}/";
        var state = CreateRandomString(32);
        var verifier = CreateRandomString(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var query = new Dictionary<string, string>
        {
            ["client_id"] = GoogleConfig.DesktopClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', scopes),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };
        var authorizationUri = AuthorizationEndpoint + "?"
            + string.Join("&", query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        if (!await Launcher.Default.OpenAsync(new Uri(authorizationUri)))
        {
            throw new GoogleAuthorizationException("Couldn't open the Google sign-in page.");
        }

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5));
        }
        catch (TimeoutException ex)
        {
            throw new GoogleAuthorizationException("Google sign-in timed out.", ex);
        }

        var queryString = context.Request.QueryString;
        var isValidState = string.Equals(queryString["state"], state, StringComparison.Ordinal);
        var code = isValidState ? queryString["code"] : null;
        await CompleteBrowserResponseAsync(context.Response, code is not null);
        if (!isValidState || string.IsNullOrWhiteSpace(code))
        {
            throw new GoogleAuthorizationException("Google sign-in did not complete.");
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = GoogleConfig.DesktopClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        });

        using var response = await _httpClient.PostAsync(TokenEndpoint, content);
        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleAuthorizationException("Google could not complete sign-in.");
        }

        return await ReadTokenResponseAsync(response, refreshToken: null);
    }

    private static async Task CompleteBrowserResponseAsync(HttpListenerResponse response, bool success)
    {
        response.StatusCode = success ? 200 : 400;
        response.ContentType = "text/html; charset=utf-8";
        var message = success
            ? "Sign-in complete. You can return to Password Vault."
            : "Sign-in failed. You can close this window.";
        var bytes = Encoding.UTF8.GetBytes(
            $"<!doctype html><html><meta charset=\"utf-8\"><title>Password Vault</title><body>{message}</body></html>");
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task<CachedToken> ReadTokenResponseAsync(
        HttpResponseMessage response,
        string? refreshToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty("access_token", out var accessTokenValue)
                || accessTokenValue.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("expires_in", out var expiresValue)
                || !expiresValue.TryGetInt32(out var expiresIn))
            {
                throw new JsonException("The token response is incomplete.");
            }

            var accessToken = accessTokenValue.GetString();
            var returnedRefreshToken = root.TryGetProperty("refresh_token", out var refresh)
                && refresh.ValueKind == JsonValueKind.String
                    ? refresh.GetString()
                    : refreshToken;
            if (string.IsNullOrWhiteSpace(accessToken) || expiresIn <= 0)
            {
                throw new JsonException("The token response is incomplete.");
            }

            return new CachedToken(accessToken, returnedRefreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
        }
        catch (JsonException ex)
        {
            throw new GoogleAuthorizationException("Google returned an invalid token response.", ex);
        }
    }

    private static string GetScopeKey(IEnumerable<string> scopes)
    {
        var joined = string.Join('\n', scopes);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
    }

    private static string CreateRandomString(int byteCount) =>
        Base64Url(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record CachedToken(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);
#endif
}
