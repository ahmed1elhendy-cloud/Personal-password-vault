using Microsoft.Maui.ApplicationModel.DataTransfer;
using PasswordVault.Core;

namespace PasswordVault.App;

public sealed class CsvExportPage : ContentPage
{
    private const string PlaintextWarning =
        "This file is not encrypted. Anyone who opens it can read every password.";

    private readonly VaultService _vaultService;
    private readonly Entry _passwordEntry = new()
    {
        Placeholder = "Master password",
        IsPassword = true
    };
    private readonly Button _exportButton = new() { Text = "Export CSV" };
    private readonly ActivityIndicator _busyIndicator = new() { IsVisible = false };

    public CsvExportPage(VaultService vaultService)
    {
        _vaultService = vaultService;
        Title = "Export CSV";

        var warning = new Label
        {
            Text = PlaintextWarning,
            TextColor = Colors.DarkRed,
            LineBreakMode = LineBreakMode.WordWrap
        };
        _exportButton.Clicked += OnExportClicked;

        Content = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 16,
            Children =
            {
                new Label { Text = "Confirm your master password to export." },
                _passwordEntry,
                warning,
                _busyIndicator,
                _exportButton
            }
        };
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        var password = _passwordEntry.Text;
        if (string.IsNullOrEmpty(password))
        {
            await DisplayAlertAsync("Password required", "Enter your master password to continue.", "OK");
            return;
        }

        _passwordEntry.Text = string.Empty;
        _exportButton.IsEnabled = false;
        _busyIndicator.IsVisible = true;
        _busyIndicator.IsRunning = true;
        try
        {
            await Task.Run(() => _vaultService.VerifyPassword(password));
        }
        catch (WrongPasswordException)
        {
            await DisplayAlertAsync("Wrong password", "The master password is incorrect.", "OK");
            return;
        }
        finally
        {
            _busyIndicator.IsRunning = false;
            _busyIndicator.IsVisible = false;
            _exportButton.IsEnabled = true;
        }

        var confirmed = await DisplayAlertAsync("Plaintext CSV", PlaintextWarning, "Continue", "Cancel");
        if (!confirmed)
        {
            return;
        }

        using var stream = new MemoryStream();
        CsvExporter.Write(_vaultService.Data.Entries, _vaultService.Data.Categories, stream);
        var csvBytes = stream.ToArray();
#if WINDOWS
        await SaveWithWindowsPickerAsync(csvBytes);
#else
        await ShareTemporaryFileAsync(csvBytes);
#endif
        await Navigation.PopAsync();
    }

#if WINDOWS
    private static async Task SaveWithWindowsPickerAsync(byte[] csvBytes)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker();
        picker.FileTypeChoices.Add("CSV file", [".csv"]);
        picker.SuggestedFileName = "password-vault-export";
        var platformWindow = Application.Current!.Windows[0].Handler!.PlatformView;
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await Windows.Storage.FileIO.WriteBytesAsync(file, csvBytes);
        }
    }
#else
    private static async Task ShareTemporaryFileAsync(byte[] csvBytes)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, $"password-vault-export-{Guid.NewGuid():N}.csv");
        try
        {
            await File.WriteAllBytesAsync(path, csvBytes);
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export password vault CSV",
                File = new ShareFile(path, "text/csv")
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
