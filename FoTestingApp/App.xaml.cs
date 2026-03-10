using System.Windows;
using FoTestingApp.Helpers;
using FoTestingApp.Services;
using FoTestingApp.Views;
using Serilog;

namespace FoTestingApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Setup Serilog
        LoggingSetup.Configure();

        // Initialize configuration
        ConfigManager.Initialize();

        Log.Information("FO Testing App started");

        // Show login window
        var loginWindow = new LoginWindow();
        loginWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("FO Testing App exiting");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
