using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using PasswordVault.App;

namespace PasswordVault.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
