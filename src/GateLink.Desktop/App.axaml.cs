using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace GateLink.Desktop;

public partial class App : Application
{
    private MainWindow? _window;
    private readonly MainViewModel _vm = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Menu bar app: no window at launch, no dock entry.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _window = new MainWindow { DataContext = _vm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayClicked(object? sender, System.EventArgs e) => TogglePopover();

    private void OnTrayOpen(object? sender, System.EventArgs e) => ShowPopover();

    private void OnTrayQuit(object? sender, System.EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void TogglePopover()
    {
        if (_window is null) return;
        if (_window.IsVisible) _window.Hide();
        else ShowPopover();
    }

    private void ShowPopover()
    {
        if (_window is null) return;
        _window.Show();
        _window.Activate();
    }
}
