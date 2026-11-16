using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using PasswordVault.Core;

namespace PasswordVault.App;

public partial class SettingsPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly GoogleAuthService _googleAuthService;
    private readonly GmailBackupSender _gmailBackupSender;
    private readonly GoogleSyncCoordinator _syncCoordinator;
    private bool _syncInProgress;
    private bool _pendingPasswordPrompt;

    public SettingsPage(
        IServiceProvider services,
        GoogleAuthService googleAuthService,
        GmailBackupSender gmailBackupSender,
        GoogleSyncCoordinator syncCoordinator)
    {
        InitializeComponent();
        _services = services;
        _googleAuthService = googleAuthService;
        _gmailBackupSender = gmailBackupSender;
        _syncCoordinator = syncCoordinator;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _syncCoordinator.StatusChanged += OnSyncStatusChanged;
        UpdateSyncStatus();
        if (_syncCoordinator.NeedsPassword)
        {
            _ = PromptForRemotePasswordAsync();
        }
    }

    protected override void OnDisappearing()
    {
        _syncCoordinator.StatusChanged -= OnSyncStatusChanged;
        base.OnDisappearing();
    }

    private async void OnCategoriesClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(_services.GetRequiredService<CategoriesPage>());
    }

    private async void OnImportCsvClicked(object? sender, EventArgs e)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a CSV file"
        });
        if (file is null)
        {
            return;
        }

        await using var source = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        await Navigation.PushAsync(new CsvImportPage(
            _services.GetRequiredService<VaultService>(),
            buffer.ToArray()));
    }

    private async void OnExportCsvClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(_services.GetRequiredService<CsvExportPage>());
    }

    private async void OnSaveBackupClicked(object? sender, EventArgs e)
    {
        var backupBytes = BackupService.ExportFileBytes(_services.GetRequiredService<VaultService>());
        var fileName = $"vault-backup-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.pvlt";
#if WINDOWS
        await SaveBackupFileAsync(backupBytes, fileName);
#else
        await ShareBackupFileAsync(backupBytes, fileName);
#endif
    }

    private async void OnImportBackupClicked(object? sender, EventArgs e)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a vault backup"
        });
        if (file is null)
        {
            return;
        }

        await using var source = await file.OpenReadAsync();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        await Navigation.PushAsync(new BackupImportPage(
            _services.GetRequiredService<VaultService>(),
            buffer.ToArray()));
    }

    private async void OnEmailBackupClicked(object? sender, EventArgs e)
    {
        SetGoogleSyncBusy(true);
        try
        {
            var vault = _services.GetRequiredService<VaultService>();
            var backupBytes = BackupService.ExportFileBytes(vault);
            var emailAddress = await _gmailBackupSender.SendAsync(backupBytes);
            await DisplayAlertAsync("Gmail backup", $"Backup sent to {emailAddress}", "OK");
        }
        catch (GmailSignInRequiredException)
        {
            await DisplayAlertAsync(
                "Gmail backup",
                "Google sign-in expired. Sign in again and retry the backup.",
                "OK");
        }
        catch (GoogleAuthorizationException)
        {
            await DisplayAlertAsync("Gmail backup", "Couldn't reach Google. Try again.", "OK");
        }
        catch (GmailBackupException)
        {
            await DisplayAlertAsync("Gmail backup", "Couldn't send the backup. Try again.", "OK");
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync("Gmail backup", "Couldn't reach Google. Try again.", "OK");
        }
        catch (OperationCanceledException)
        {
            await DisplayAlertAsync("Gmail backup", "Couldn't reach Google. Try again.", "OK");
        }
        finally
        {
            SetGoogleSyncBusy(false);
            UpdateSyncStatus();
        }
    }

    private async void OnGoogleSignInClicked(object? sender, EventArgs e)
    {
        SetGoogleSyncBusy(true);
        try
        {
            await _googleAuthService.GetAccessTokenAsync([GoogleScopes.DriveAppData]);
            _syncCoordinator.EnableSync();
            await RunSyncAsync();
        }
        catch (GoogleAuthorizationException)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
        catch (GoogleDriveException)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
        catch (OperationCanceledException)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
        finally
        {
            SetGoogleSyncBusy(false);
            UpdateSyncStatus();
        }
    }

    private async void OnGoogleSyncNowClicked(object? sender, EventArgs e)
    {
        SetGoogleSyncBusy(true);
        try
        {
            await RunSyncAsync();
        }
        catch (GoogleAuthorizationException)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
        catch (GoogleDriveException)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
        catch (OperationCanceledException)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
        finally
        {
            SetGoogleSyncBusy(false);
            UpdateSyncStatus();
        }
    }

    private async Task RunSyncAsync()
    {
        var outcome = await _syncCoordinator.SyncNowAsync();
        if (outcome == SyncOutcome.NeedsPassword)
        {
            await PromptForRemotePasswordAsync();
        }
        else if (outcome == SyncOutcome.Failed)
        {
            await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
        }
    }

    private async Task PromptForRemotePasswordAsync()
    {
        if (_pendingPasswordPrompt)
        {
            return;
        }

        _pendingPasswordPrompt = true;
        try
        {
            var password = await PasswordPromptPage.PromptAsync(
                this,
                "Other device's vault password",
                "Enter the master password used by the other device.");
            if (password is null)
            {
                return;
            }

            try
            {
                var outcome = await _syncCoordinator.SyncNowAsync(password);
                if (outcome == SyncOutcome.Failed)
                {
                    await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
                }
            }
            catch (WrongPasswordException)
            {
                await DisplayAlertAsync("Google Drive", "The other device's master password is incorrect.", "OK");
            }
            catch (GoogleAuthorizationException)
            {
                await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
            }
            catch (GoogleDriveException)
            {
                await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
            }
            catch (HttpRequestException)
            {
                await DisplayAlertAsync("Google Drive", "Couldn't reach Google. Try again.", "OK");
            }
        }
        finally
        {
            _pendingPasswordPrompt = false;
            UpdateSyncStatus();
        }
    }

    private void UpdateSyncStatus()
    {
        GoogleSyncStatusLabel.Text = _syncCoordinator.Status;
        if (string.IsNullOrEmpty(GoogleSyncStatusLabel.Text))
        {
            GoogleSyncStatusLabel.Text = _syncCoordinator.IsEnabled ? "Google Drive sync is on." : "Sync is off.";
        }

        LastSyncedLabel.Text = _syncCoordinator.LastSynced is { } lastSynced
            ? $"Last synced: {lastSynced.ToLocalTime():g}"
            : "Not synced yet.";
        GoogleSyncNowButton.IsEnabled = _syncCoordinator.IsEnabled && !_syncInProgress;
        GoogleSignInButton.IsEnabled = !_syncInProgress;
    }

    private void SetGoogleSyncBusy(bool isBusy)
    {
        _syncInProgress = isBusy;
        GoogleSyncBusyIndicator.IsVisible = isBusy;
        GoogleSyncBusyIndicator.IsRunning = isBusy;
        UpdateSyncStatus();
    }

    private void OnSyncStatusChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateSyncStatus();
            if (IsVisible && _syncCoordinator.NeedsPassword && !_syncInProgress)
            {
                _ = PromptForRemotePasswordAsync();
            }
        });
    }

#if WINDOWS
    private static async Task SaveBackupFileAsync(byte[] backupBytes, string fileName)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.FileTypeChoices.Add("Password Vault backup", [".pvlt"]);
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(fileName);
        var platformWindow = Application.Current!.Windows[0].Handler!.PlatformView;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await Windows.Storage.FileIO.WriteBytesAsync(file, backupBytes);
        }
    }
#else
    private static async Task ShareBackupFileAsync(byte[] backupBytes, string fileName)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        try
        {
            await File.WriteAllBytesAsync(path, backupBytes);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Save Password Vault backup",
                File = new ShareFile(path, "application/octet-stream")
            });
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
#endif
}
