using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;

namespace GateLink.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Global safety nets: log unexpected exceptions to a file instead of
        // letting them abort the process (which showed up as a crash after
        // connect). This keeps the app alive and gives us a diagnosable trail.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogFatal("AppDomain", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogFatal("UnobservedTask", e.Exception);
            e.SetObserved();   // prevents the finalizer from escalating to a crash
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogFatal("Startup", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void LogFatal(string source, Exception? ex)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = Path.Combine(home, "gatelink-crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {ex}\n\n");
        }
        catch
        {
            // nothing more we can do
        }
    }
}
