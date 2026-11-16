using Microsoft.Extensions.DependencyInjection;
using PasswordVault.Core;

namespace PasswordVault.App;

public partial class EntryDetailPage : ContentPage
{
    private readonly VaultService _vaultService;
    private readonly IClipboardService _clipboardService;
    private readonly IServiceProvider _services;
    private VaultEntry? _entry;

    public EntryDetailPage(
        VaultService vaultService,
        IClipboardService clipboardService,
        IServiceProvider services)
    {
        InitializeComponent();
        _vaultService = vaultService;
        _clipboardService = clipboardService;
        _services = services;
    }

    public void LoadEntry(VaultEntry entry)
    {
        _entry = entry;
        Title = entry.Title;
        DetailHost.Children.Clear();
        var view = new EntryDetailView(_vaultService, _clipboardService);
        view.LoadEntry(entry);
        DetailHost.Children.Add(view);

        ToolbarItems.Clear();
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Edit",
            Order = ToolbarItemOrder.Primary,
            Command = new Command(async () =>
            {
                var editPage = _services.GetRequiredService<EntryEditPage>();
                editPage.LoadEntry(entry);
                await Navigation.PushAsync(editPage);
            })
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_entry?.IsDeleted == true)
        {
            await Navigation.PopAsync();
            return;
        }

        if (_entry is not null)
        {
            LoadEntry(_entry);
        }
    }
}
