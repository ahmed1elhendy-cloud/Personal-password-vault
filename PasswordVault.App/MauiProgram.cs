using PasswordVault.Core;

namespace PasswordVault.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IVaultStore>(_ =>
            new FileVaultStore(Path.Combine(FileSystem.AppDataDirectory, "vault.pvlt")));
        builder.Services.AddSingleton<BiometricPolicy>();
#if ANDROID
        builder.Services.AddSingleton<IBiometricUnlock, AndroidBiometricUnlock>();
#elif WINDOWS
        builder.Services.AddSingleton<IBiometricUnlock, UnavailableBiometricUnlock>();
#endif
        builder.Services.AddSingleton<VaultService>();
        builder.Services.AddSingleton<AutoLockService>();
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<GoogleAuthService>();
        builder.Services.AddSingleton<GmailBackupSender>();
        builder.Services.AddSingleton<GoogleDriveFileStore>();
        builder.Services.AddSingleton<ICloudFileStore>(services =>
            services.GetRequiredService<GoogleDriveFileStore>());
        builder.Services.AddSingleton<GoogleSyncCoordinator>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddTransient<SetupPage>();
        builder.Services.AddTransient<UnlockPage>();
        builder.Services.AddTransient<VaultListViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<CsvExportPage>();
        builder.Services.AddTransient<CategoriesPage>();
        builder.Services.AddTransient<EntryDetailPage>();
        builder.Services.AddTransient<EntryEditPage>();

        return builder.Build();
    }
}
