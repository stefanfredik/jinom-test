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

        // Global exception handlers
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show(
                $"Terjadi kesalahan: {args.Exception.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Unhandled domain exception");
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

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
