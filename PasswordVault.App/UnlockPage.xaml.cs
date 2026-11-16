using PasswordVault.Core;

namespace PasswordVault.App;

public partial class UnlockPage : ContentPage
{
    private readonly VaultService _vaultService;

    public UnlockPage(VaultService vaultService)
    {
        InitializeComponent();
        _vaultService = vaultService;
    }

    private async void OnUnlockClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        var password = PasswordEntry.Text ?? string.Empty;

        SetBusy(true);
        try
        {
            await Task.Run(() => _vaultService.Unlock(password));
        }
        catch (WrongPasswordException)
        {
            ErrorLabel.Text = "Wrong password";
            ErrorLabel.IsVisible = true;
        }
        catch (InvalidVaultFileException)
        {
            ErrorLabel.Text = "The vault file is invalid.";
            ErrorLabel.IsVisible = true;
        }
        catch (IOException)
        {
            ErrorLabel.Text = "The vault file could not be read.";
            ErrorLabel.IsVisible = true;
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
        UnlockButton.IsEnabled = !isBusy;
        PasswordEntry.IsEnabled = !isBusy;
    }
}
