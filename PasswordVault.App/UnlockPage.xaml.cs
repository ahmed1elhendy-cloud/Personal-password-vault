using PasswordVault.Core;
using System.Security.Cryptography;

namespace PasswordVault.App;

public partial class UnlockPage : ContentPage
{
    private readonly VaultService _vaultService;
    private readonly IBiometricUnlock _biometricUnlock;
    private readonly BiometricPolicy _biometricPolicy;

    public UnlockPage(
        VaultService vaultService,
        IBiometricUnlock biometricUnlock,
        BiometricPolicy biometricPolicy)
    {
        InitializeComponent();
        _vaultService = vaultService;
        _biometricUnlock = biometricUnlock;
        _biometricPolicy = biometricPolicy;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateFingerprintButton();
    }

    private async void OnUnlockClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        var password = PasswordEntry.Text ?? string.Empty;

        SetBusy(true);
        try
        {
            await Task.Run(() => _vaultService.Unlock(password));
            _biometricPolicy.RecordPasswordUnlock();
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

    private async void OnFingerprintUnlockClicked(object? sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        SetBusy(true);
        VaultKey? key = null;
        try
        {
            key = await _biometricUnlock.UnlockAsync();
            if (key is null)
            {
                if (_biometricPolicy.FailedFingerprintAttempts >= BiometricPolicy.MaximumFingerprintAttempts)
                {
                    ErrorLabel.Text = "Too many attempts. Enter your master password.";
                    ErrorLabel.IsVisible = true;
                }

                return;
            }

            var unlockedKey = key;
            await Task.Run(() => _vaultService.UnlockWithKey(unlockedKey));
            key = null;
        }
        catch (WrongPasswordException)
        {
            ErrorLabel.Text = "Fingerprint unlock failed. Enter your master password.";
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
        catch (InvalidOperationException)
        {
            ErrorLabel.Text = "Fingerprint unlock is unavailable. Enter your master password.";
            ErrorLabel.IsVisible = true;
        }
        catch (CryptographicException)
        {
            ErrorLabel.Text = "Fingerprint unlock failed. Enter your master password.";
            ErrorLabel.IsVisible = true;
        }
        finally
        {
            key?.Dispose();
            SetBusy(false);
            UpdateFingerprintButton();
        }
    }

    private void SetBusy(bool isBusy)
    {
        BusyIndicator.IsVisible = isBusy;
        BusyIndicator.IsRunning = isBusy;
        UnlockButton.IsEnabled = !isBusy;
        FingerprintButton.IsEnabled = !isBusy;
        PasswordEntry.IsEnabled = !isBusy;
    }

    private void UpdateFingerprintButton()
    {
        var isAvailable = _biometricUnlock.IsAvailable;
        FingerprintButton.IsVisible = isAvailable
            && _biometricPolicy.CanOfferFingerprint(isAvailable, _biometricUnlock.IsEnabled);
    }
}
