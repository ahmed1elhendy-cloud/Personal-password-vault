using Microsoft.Extensions.DependencyInjection;
using PasswordVault.Core;

namespace PasswordVault.App;

public partial class MainPage : ContentPage
{
    private readonly VaultListView _vaultListView;
    private readonly IServiceProvider _services;

    public MainPage(
        VaultService vaultService,
        VaultListViewModel viewModel,
        IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _vaultListView = new VaultListView(viewModel);
        _vaultListView.AddRequested += OnAddRequested;
        PageContent.Children.Add(_vaultListView);

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Settings",
            Order = ToolbarItemOrder.Primary,
            Priority = 0,
            Command = new Command(async () =>
                await Navigation.PushAsync(_services.GetRequiredService<SettingsPage>()))
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vaultListView.Refresh();
    }

    private async void OnAddRequested(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(_services.GetRequiredService<EntryEditPage>());
    }
}
