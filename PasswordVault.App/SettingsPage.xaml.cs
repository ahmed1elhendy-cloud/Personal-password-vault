using Microsoft.Extensions.DependencyInjection;
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
}
