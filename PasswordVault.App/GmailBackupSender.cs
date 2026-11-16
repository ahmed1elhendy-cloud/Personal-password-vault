using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PasswordVault.Core;

namespace PasswordVault.App;

internal sealed class GmailBackupException : Exception
{
    internal GmailBackupException(string message) : base(message) { }
    internal GmailBackupException(string message, Exception innerException) : base(message, innerException) { }
}

internal sealed class GmailSignInRequiredException : Exception
{
    internal GmailSignInRequiredException(string message, Exception innerException) : base(message, innerException) { }
}

internal sealed class GoogleAccessTokenRejectedException : Exception { }

public sealed class GmailBackupSender
{
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
    private const string SendEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/messages/send";
    private static readonly string[] GmailScopes = [GoogleScopes.GmailSend, GoogleScopes.Email];
    private readonly GoogleAuthService _authService;
    private readonly HttpClient _httpClient;

    public GmailBackupSender(GoogleAuthService authService, HttpClient httpClient)
    {
        _authService = authService;
        _httpClient = httpClient;
    }

    public async Task<string> SendAsync(byte[] vaultFile)
    {
        ArgumentNullException.ThrowIfNull(vaultFile);
        var accessToken = await _authService.GetAccessTokenAsync(GmailScopes);
        try
        {
            return await SendWithTokenAsync(vaultFile, accessToken);
        }
        catch (GoogleAccessTokenRejectedException)
        {
            try
            {
                accessToken = await _authService.RefreshAccessTokenAsync(GmailScopes);
            }
            catch (GoogleAuthorizationException ex)
            {
                throw new GmailSignInRequiredException(
                    "Google sign-in expired. Sign in again and retry the backup.",
                    ex);
            }

            try
            {
                return await SendWithTokenAsync(vaultFile, accessToken);
            }
            catch (GoogleAccessTokenRejectedException ex)
            {
                throw new GmailSignInRequiredException(
                    "Google sign-in expired. Sign in again and retry the backup.",
                    ex);
            }
        }
    }

    private async Task<string> SendWithTokenAsync(byte[] vaultFile, string accessToken)
    {
        var emailAddress = await GetEmailAddressAsync(accessToken);
        var fileName = $"vault-backup-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.pvlt";
        var message = MimeMessageBuilder.Build(
            emailAddress,
            emailAddress,
            "Password vault backup",
            "Encrypted backup. Open it with Import backup file in the app. It needs your master password.",
            fileName,
            vaultFile);
        var payload = JsonSerializer.Serialize(new { raw = MimeMessageBuilder.ToBase64Url(message) });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new GoogleAccessTokenRejectedException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GmailBackupException("Gmail couldn't send the backup.");
        }

        return emailAddress;
    }

    private async Task<string> GetEmailAddressAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new GoogleAccessTokenRejectedException();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GmailBackupException("Google couldn't provide the account email.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (document.RootElement.TryGetProperty("email", out var emailElement)
                && emailElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(emailElement.GetString()))
            {
                return emailElement.GetString()!;
            }

            throw new GmailBackupException("Google didn't provide an account email.");
        }
        catch (JsonException ex)
        {
            throw new GmailBackupException("Google returned invalid account information.", ex);
        }
    }
}
