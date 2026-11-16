using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;
using Microsoft.Extensions.DependencyInjection;
using PasswordVault.Core;

namespace PasswordVault.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly VaultService _vaultService;
    private readonly AutoLockService _autoLockService;

    public App(IServiceProvider services, VaultService vaultService, AutoLockService autoLockService)
    {
        InitializeComponent();
        _services = services;
        _vaultService = vaultService;
        _autoLockService = autoLockService;
        _ = services.GetRequiredService<GoogleSyncCoordinator>();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Page initialPage = _vaultService.HasVault
            ? _services.GetRequiredService<UnlockPage>()
            : _services.GetRequiredService<SetupPage>();
        var window = new Window(initialPage);
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            const string widthKey = "windows.window.width";
            const string heightKey = "windows.window.height";
            var preferences = Preferences.Default;
            window.MinimumWidth = 900;
            window.MinimumHeight = 600;
            window.Width = Math.Max(900, preferences.Get(widthKey, 1100d));
            window.Height = Math.Max(600, preferences.Get(heightKey, 700d));
            window.SizeChanged += (_, _) =>
            {
                if (window.Width >= 900 && window.Height >= 600)
                {
                    preferences.Set(widthKey, window.Width);
                    preferences.Set(heightKey, window.Height);
                }
            };
        }

        _vaultService.Unlocked += (_, _) =>
            MainThread.BeginInvokeOnMainThread(() =>
                window.Page = new NavigationPage(_services.GetRequiredService<MainPage>()));
        _vaultService.Locked += (_, _) =>
            MainThread.BeginInvokeOnMainThread(() =>
                window.Page = _services.GetRequiredService<UnlockPage>());

        var syncCoordinator = _services.GetRequiredService<GoogleSyncCoordinator>();
        window.Stopped += (_, _) => _autoLockService.OnBackgrounded();
        window.Resumed += (_, _) =>
        {
            _autoLockService.OnResumed();
            if (!_vaultService.IsLocked)
            {
                syncCoordinator.ScheduleSync(TimeSpan.Zero);
            }
        };
        return window;
    }
}
