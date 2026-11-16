using PasswordVault.Core;

namespace PasswordVault.App;

public partial class SetupPage : ContentPage
{
    private readonly VaultService _vaultService;

    public SetupPage(VaultService vaultService)
    {
        InitializeComponent();
        _vaultService = vaultService;
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

    private void SetBusy(bool isBusy)
    {
        BusyIndicator.IsVisible = isBusy;
        BusyIndicator.IsRunning = isBusy;
        CreateButton.IsEnabled = !isBusy;
        PasswordEntry.IsEnabled = !isBusy;
        ConfirmPasswordEntry.IsEnabled = !isBusy;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
