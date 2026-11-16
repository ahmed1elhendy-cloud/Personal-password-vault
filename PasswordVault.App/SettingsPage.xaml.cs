using Microsoft.Extensions.DependencyInjection;

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
}
