using PasswordVault.Core;

namespace PasswordVault.App;

public partial class SetupPage : ContentPage
{
    private readonly VaultService _vaultService;
    private readonly GoogleDriveFileStore _googleDrive;
    private readonly GoogleSyncCoordinator _syncCoordinator;

    public SetupPage(
        VaultService vaultService,
        GoogleDriveFileStore googleDrive,
        GoogleSyncCoordinator syncCoordinator)
    {
        InitializeComponent();
        _vaultService = vaultService;
        _googleDrive = googleDrive;
        _syncCoordinator = syncCoordinator;
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        var password = PasswordEntry.Text ?? string.Empty;
        if (password.Length < 10)
        {
            ShowError("Use a password with at least 10 characters.");
            return;
        }

        if (!string.Equals(password, ConfirmPasswordEntry.Text, StringComparison.Ordinal))
        {
            ShowError("Passwords do not match.");
            return;
        }

        SetBusy(true);
        try
        {
            await Task.Run(() => _vaultService.Create(password));
        }
        catch (ArgumentException)
        {
            ShowError("Use a password with at least 10 characters.");
        }
        catch (InvalidOperationException)
        {
            ShowError("A vault already exists. Restart the app to unlock it.");
        }
        catch (IOException)
        {
            ShowError("The vault could not be saved. Please try again.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRestoreFromGoogleClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        SetBusy(true);
        try
        {
            var remote = await _googleDrive.DownloadAsync(CancellationToken.None);
            if (remote is null)
            {
                ShowError("No vault backup was found in Google Drive.");
                return;
            }

            var password = await PasswordPromptPage.PromptAsync(
                this,
                "Restore from Google Drive",
                "Enter the master password for this vault.");
            if (password is null)
            {
                return;
            }

            await Task.Run(() => _vaultService.RestoreFromFile(remote.Data, password));
            _syncCoordinator.EnableSync();
            _syncCoordinator.ScheduleSync(TimeSpan.Zero);
        }
        catch (WrongPasswordException)
        {
            ShowError("That master password is incorrect.");
        }
        catch (GoogleAuthorizationException)
        {
            ShowError("Couldn't reach Google. Try again.");
        }
        catch (GoogleDriveException)
        {
            ShowError("Couldn't reach Google. Try again.");
        }
        catch (HttpRequestException)
        {
            ShowError("Couldn't reach Google. Try again.");
        }
        catch (OperationCanceledException)
        {
            ShowError("Couldn't reach Google. Try again.");
        }
        catch (IOException)
        {
            ShowError("The vault could not be restored. Please try again.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        BusyIndicator.IsVisible = isBusy;
        BusyIndicator.IsRunning = isBusy;
        CreateButton.IsEnabled = !isBusy;
        RestoreButton.IsEnabled = !isBusy;
        PasswordEntry.IsEnabled = !isBusy;
        ConfirmPasswordEntry.IsEnabled = !isBusy;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
