using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PasswordVault.Core;

namespace PasswordVault.App;

public sealed class GoogleDriveFileStore : ICloudFileStore
{
    private const string DriveApi = "https://www.googleapis.com/drive/v3";
    private const string UploadApi = "https://www.googleapis.com/upload/drive/v3";
    private readonly GoogleAuthService _authService;
    private readonly HttpClient _httpClient;

    public GoogleDriveFileStore(GoogleAuthService authService, HttpClient httpClient)
    {
        _authService = authService;
        _httpClient = httpClient;
    }

    public async Task<RemoteFile?> DownloadAsync(CancellationToken cancellationToken)
    {
        var accessToken = await _authService.GetAccessTokenAsync([GoogleScopes.DriveAppData]);
        var file = await FindFileAsync(accessToken, cancellationToken);
        if (file is null)
        {
            return null;
        }

        using var request = CreateRequest(
            HttpMethod.Get,
            $"{DriveApi}/files/{Uri.EscapeDataString(file.Id)}?alt=media",
            accessToken);
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return new RemoteFile(data, file.Version);
    }

    public async Task<bool> UploadAsync(
        byte[] data,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        var accessToken = await _authService.GetAccessTokenAsync([GoogleScopes.DriveAppData]);
        var existingFile = await FindFileAsync(accessToken, cancellationToken);
        if (expectedVersion is null
            ? existingFile is not null
            : existingFile is null || !string.Equals(existingFile.Version, expectedVersion, StringComparison.Ordinal))
        {
            return false;
        }

        var metadata = existingFile is null
            ? JsonSerializer.Serialize(new { name = "vault.pvlt", parents = new[] { "appDataFolder" } })
            : JsonSerializer.Serialize(new { name = "vault.pvlt" });
        using var multipart = new MultipartContent("related");
        multipart.Add(new StringContent(metadata, Encoding.UTF8, "application/json"));
        var fileContent = new ByteArrayContent(data);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent);

        var endpoint = existingFile is null
            ? $"{UploadApi}/files?uploadType=multipart&fields=id,version"
            : $"{UploadApi}/files/{Uri.EscapeDataString(existingFile.Id)}?uploadType=multipart&fields=id,version";
        using var request = CreateRequest(existingFile is null ? HttpMethod.Post : HttpMethod.Patch, endpoint, accessToken);
        request.Content = multipart;
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response);
        return true;
    }

    private async Task<DriveFile?> FindFileAsync(string accessToken, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(
            "name = 'vault.pvlt' and 'appDataFolder' in parents and trashed = false");
        var uri = $"{DriveApi}/files?q={query}&spaces=appDataFolder&fields=files(id,version)&pageSize=1";
        using var request = CreateRequest(HttpMethod.Get, uri, accessToken);
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response);

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("files", out var files)
                || files.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("The Drive file list is invalid.");
            }

            if (files.GetArrayLength() == 0)
            {
                return null;
            }

            var file = files[0];
            if (!file.TryGetProperty("id", out var idValue)
                || !file.TryGetProperty("version", out var versionValue))
            {
                throw new JsonException("The Drive file metadata is incomplete.");
            }

            var id = idValue.GetString();
            var version = versionValue.ToString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            {
                throw new JsonException("The Drive file metadata is incomplete.");
            }

            return new DriveFile(id, version);
        }
        catch (JsonException ex)
        {
            throw new GoogleDriveException("Google Drive returned invalid file metadata.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GoogleDriveException("Couldn't reach Google Drive.", ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new GoogleDriveException("Google Drive request failed.");
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string accessToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private sealed record DriveFile(string Id, string Version);
}
