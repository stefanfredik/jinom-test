using System.Globalization;
using System.Windows.Markup;
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

        // One culture for the whole UI: XAML StringFormat follows FrameworkElement.Language (en-US by
        // default) while code follows the thread culture, so dates showed "Aug" on one screen and
        // "Agu" on another. The app is Indonesian throughout.
        var culture = CultureInfo.GetCultureInfo("id-ID");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

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
