using Microsoft.Maui.ApplicationModel;
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
