using PasswordVault.Core;
using Microsoft.Maui.Storage;

namespace PasswordVault.App;

public sealed class GoogleSyncCoordinator
{
    public const string EnabledPreferenceKey = "google.drive.sync.enabled";
    private const string LastSyncedPreferenceKey = "google.drive.sync.last";
    private const string StatusPreferenceKey = "google.drive.sync.status";
    private const string NeedsPasswordPreferenceKey = "google.drive.sync.needs-password";
    private readonly VaultService _vaultService;
    private readonly ICloudFileStore _cloudFileStore;
    private readonly object _scheduleLock = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource? _scheduledSync;

    public GoogleSyncCoordinator(VaultService vaultService, ICloudFileStore cloudFileStore)
    {
        _vaultService = vaultService;
        _cloudFileStore = cloudFileStore;
        _vaultService.Saved += OnVaultSaved;
        _vaultService.Unlocked += OnVaultUnlocked;
    }

    public event EventHandler? StatusChanged;

    public bool IsEnabled => Preferences.Default.Get(EnabledPreferenceKey, false);

    public DateTimeOffset? LastSynced
    {
        get
        {
            var value = Preferences.Default.Get(LastSyncedPreferenceKey, string.Empty);
            return DateTimeOffset.TryParse(value, out var timestamp) ? timestamp : null;
        }
    }

    public string Status => Preferences.Default.Get(StatusPreferenceKey, string.Empty);
    public bool NeedsPassword => Preferences.Default.Get(NeedsPasswordPreferenceKey, false);

    public void EnableSync()
    {
        Preferences.Default.Set(EnabledPreferenceKey, true);
    }

    public void ScheduleSync(TimeSpan delay)
    {
        if (!IsEnabled)
        {
            return;
        }

        CancellationToken token;
        lock (_scheduleLock)
        {
            _scheduledSync?.Cancel();
            _scheduledSync?.Dispose();
            _scheduledSync = new CancellationTokenSource();
            token = _scheduledSync.Token;
        }

        _ = RunScheduledSyncAsync(delay, token);
    }

    public async Task<SyncOutcome> SyncNowAsync(string? remotePassword = null)
    {
        if (_vaultService.IsLocked)
        {
            throw new InvalidOperationException("Unlock the vault before syncing.");
        }

        await _syncGate.WaitAsync();
        try
        {
            var outcome = await SyncService.SyncAsync(
                _vaultService,
                _cloudFileStore,
                remotePassword,
                CancellationToken.None);
            if (outcome is SyncOutcome.Uploaded or SyncOutcome.Merged)
            {
                var now = DateTimeOffset.UtcNow;
                Preferences.Default.Set(LastSyncedPreferenceKey, now.ToString("O"));
                Preferences.Default.Set(NeedsPasswordPreferenceKey, false);
                SetStatus($"Last synced {now.ToLocalTime():g}");
            }
            else if (outcome == SyncOutcome.NeedsPassword)
            {
                Preferences.Default.Set(NeedsPasswordPreferenceKey, true);
                SetStatus("Enter the other device's master password to finish syncing.");
            }
            else
            {
                Preferences.Default.Set(NeedsPasswordPreferenceKey, false);
                SetStatus("Couldn't reach Google. Try again.");
            }

            return outcome;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private void OnVaultSaved(object? sender, EventArgs e) =>
        ScheduleSync(TimeSpan.FromSeconds(10));

    private void OnVaultUnlocked(object? sender, EventArgs e) =>
        ScheduleSync(TimeSpan.FromSeconds(1));

    private async Task RunScheduledSyncAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            if (!IsEnabled || _vaultService.IsLocked)
            {
                return;
            }

            await SyncNowAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            SetStatus("Couldn't reach Google. Try again.");
        }
        catch (GoogleAuthorizationException)
        {
            SetStatus("Couldn't reach Google. Try again.");
        }
        catch (GoogleDriveException)
        {
            SetStatus("Couldn't reach Google. Try again.");
        }
        catch (HttpRequestException)
        {
            SetStatus("Couldn't reach Google. Try again.");
        }
        catch (InvalidOperationException) when (_vaultService.IsLocked)
        {
        }
    }

    private void SetStatus(string status)
    {
        Preferences.Default.Set(StatusPreferenceKey, status);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
