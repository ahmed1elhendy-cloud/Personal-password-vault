using PasswordVault.Core;

namespace PasswordVault.App;

public sealed class BackupImportPage : ContentPage
{
    private readonly VaultService _vaultService;
    private readonly byte[] _backupFile;
    private readonly Entry _passwordEntry = new()
    {
        Placeholder = "Backup password",
        IsPassword = true,
        ReturnType = ReturnType.Done
    };
    private readonly Button _importButton = new() { Text = "Import backup" };
    private readonly ActivityIndicator _busyIndicator = new() { IsVisible = false };

    public BackupImportPage(VaultService vaultService, byte[] backupFile)
    {
        _vaultService = vaultService;
        _backupFile = backupFile;
        Title = "Import backup file";
        _importButton.Clicked += OnImportClicked;

        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 16,
            Children =
            {
                new Label { Text = "Enter the password used to create this backup." },
                _passwordEntry,
                _busyIndicator,
                _importButton
            }
        };
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        var backupPassword = _passwordEntry.Text;
        if (string.IsNullOrEmpty(backupPassword))
        {
            await DisplayAlertAsync("Password required", "Enter the backup password to continue.", "OK");
            return;
        }

        _passwordEntry.Text = string.Empty;
        _importButton.IsEnabled = false;
        _busyIndicator.IsVisible = true;
        _busyIndicator.IsRunning = true;
        try
        {
            var changedCount = await Task.Run(() =>
                BackupService.Import(_vaultService, _backupFile, backupPassword));
            await DisplayAlertAsync(
                "Backup imported",
                $"{changedCount} entries added or updated.",
                "OK");
            await Navigation.PopAsync();
        }
        catch (WrongPasswordException)
        {
            await DisplayAlertAsync("Wrong password", "The backup password is incorrect.", "OK");
        }
        catch (InvalidVaultFileException)
        {
            await DisplayAlertAsync("Invalid backup", "The selected file is not a valid vault backup.", "OK");
        }
        finally
        {
            _busyIndicator.IsRunning = false;
            _busyIndicator.IsVisible = false;
            _importButton.IsEnabled = true;
        }
    }
}
