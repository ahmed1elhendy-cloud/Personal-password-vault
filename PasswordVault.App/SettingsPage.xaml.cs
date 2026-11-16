using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using PasswordVault.Core;

namespace PasswordVault.App;

public partial class SettingsPage : ContentPage
{
    private readonly IServiceProvider _services;

    public SettingsPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
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
