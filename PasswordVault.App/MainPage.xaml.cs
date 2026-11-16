using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Devices;
using PasswordVault.Core;
#if WINDOWS
using Windows.System;
using WinUIKeyboardAccelerator = Microsoft.UI.Xaml.Input.KeyboardAccelerator;
using WinUIKeyboardAcceleratorInvokedEventArgs = Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs;
#endif

namespace PasswordVault.App;

public partial class MainPage : ContentPage
{
    private readonly VaultListView _vaultListView;
    private readonly VaultService _vaultService;
    private readonly IClipboardService _clipboardService;
    private readonly IServiceProvider _services;
    private readonly bool _isWindows;
    private Grid? _detailHost;
    private EntryDetailView? _detailView;
    private VaultEntry? _selectedEntry;
#if WINDOWS
    private Microsoft.UI.Xaml.UIElement? _nativeKeyboardRoot;
    private readonly List<WinUIKeyboardAccelerator> _keyboardAccelerators = [];
#endif

    public MainPage(
        VaultService vaultService,
        VaultListViewModel viewModel,
        IClipboardService clipboardService,
        IServiceProvider services)
    {
        InitializeComponent();
        _vaultService = vaultService;
        _clipboardService = clipboardService;
        _services = services;
        _isWindows = DeviceInfo.Platform == DevicePlatform.WinUI;
        _vaultListView = new VaultListView(viewModel);
        _vaultListView.AddRequested += OnAddRequested;
        _vaultListView.EntrySelected += OnEntrySelected;

        if (_isWindows)
        {
            CreateWindowsSplitView();
            AddWindowsToolbar();
        }
        else
        {
            PageContent.Children.Add(_vaultListView);
            ToolbarItems.Add(new ToolbarItem
            {
                Text = "Settings",
                Order = ToolbarItemOrder.Primary,
                Priority = 0,
                Command = new Command(async () => await OpenSettingsAsync())
            });
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vaultListView.Refresh();
        if (_isWindows && _selectedEntry is not null)
        {
            RefreshWindowsDetail();
        }
    }

#if WINDOWS
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (_isWindows)
        {
            AttachKeyboardShortcuts();
        }
    }
#endif

    private void CreateWindowsSplitView()
    {
        var splitView = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(340)),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 0
        };
        _detailHost = new Grid();
        ShowEmptyDetail();
        splitView.Add(_vaultListView, 0, 0);
        splitView.Add(_detailHost, 1, 0);
        PageContent.Children.Clear();
        PageContent.Children.Add(splitView);
    }

    private void AddWindowsToolbar()
    {
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Add",
            Order = ToolbarItemOrder.Primary,
            Priority = 0,
            Command = new Command(async () => await OpenAddEntryAsync())
        });
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Settings",
            Order = ToolbarItemOrder.Primary,
            Priority = 1,
            Command = new Command(async () => await OpenSettingsAsync())
        });
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Lock",
            Order = ToolbarItemOrder.Primary,
            Priority = 2,
            Command = new Command(LockVault)
        });
    }

    private async void OnAddRequested(object? sender, EventArgs e)
    {
        await OpenAddEntryAsync();
    }

    private async void OnEntrySelected(object? sender, VaultEntrySelectedEventArgs e)
    {
        if (_isWindows)
        {
            ShowEntryDetail(e.Entry);
            return;
        }

        var page = _services.GetRequiredService<EntryDetailPage>();
        page.LoadEntry(e.Entry);
        await Navigation.PushAsync(page);
    }

    private async Task OpenAddEntryAsync()
    {
        await Navigation.PushAsync(_services.GetRequiredService<EntryEditPage>());
    }

    private async Task OpenSettingsAsync()
    {
        await Navigation.PushAsync(_services.GetRequiredService<SettingsPage>());
    }

    private void LockVault()
    {
        _vaultService.Lock();
    }

    private void RefreshWindowsDetail()
    {
        if (_selectedEntry is null)
        {
            return;
        }

        var entry = _vaultService.Data.Entries.FirstOrDefault(
            item => item.Id == _selectedEntry.Id && !item.IsDeleted);
        if (entry is null)
        {
            _selectedEntry = null;
            _detailView = null;
            ShowEmptyDetail();
            return;
        }

        ShowEntryDetail(entry);
    }

    private void ShowEntryDetail(VaultEntry entry)
    {
        _selectedEntry = entry;
        _detailView ??= new EntryDetailView(_vaultService, _clipboardService);
        _detailView.LoadEntry(entry);
        _detailHost!.Children.Clear();
        _detailHost.Children.Add(_detailView);
    }

    private void ShowEmptyDetail()
    {
        if (_detailHost is null)
        {
            return;
        }

        _detailHost.Children.Clear();
        _detailHost.Children.Add(new Label
        {
            HorizontalOptions = LayoutOptions.Center,
            Text = "Select an entry",
            TextColor = Color.FromArgb("#52666E"),
            VerticalOptions = LayoutOptions.Center
        });
    }

#if WINDOWS
    private void AttachKeyboardShortcuts()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement root
            || ReferenceEquals(root, _nativeKeyboardRoot))
        {
            return;
        }

        if (_nativeKeyboardRoot is not null)
        {
            foreach (var accelerator in _keyboardAccelerators)
            {
                _nativeKeyboardRoot.KeyboardAccelerators.Remove(accelerator);
            }
        }

        _nativeKeyboardRoot = root;
        _keyboardAccelerators.Clear();
        AddKeyboardAccelerator(root, VirtualKey.F);
        AddKeyboardAccelerator(root, VirtualKey.L);
        AddKeyboardAccelerator(root, VirtualKey.N);
    }

    private void AddKeyboardAccelerator(Microsoft.UI.Xaml.UIElement root, VirtualKey key)
    {
        var accelerator = new WinUIKeyboardAccelerator
        {
            Key = key,
            Modifiers = VirtualKeyModifiers.Control
        };
        accelerator.Invoked += OnKeyboardAcceleratorInvoked;
        root.KeyboardAccelerators.Add(accelerator);
        _keyboardAccelerators.Add(accelerator);
    }

    private void OnKeyboardAcceleratorInvoked(
        WinUIKeyboardAccelerator sender,
        WinUIKeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        switch (sender.Key)
        {
            case VirtualKey.F:
                _vaultListView.FocusSearch();
                break;
            case VirtualKey.L:
                LockVault();
                break;
            case VirtualKey.N:
                _ = OpenAddEntryAsync();
                break;
        }
    }
#endif
}
