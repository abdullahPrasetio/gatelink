using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace GateLink.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Position the popover just below the menu-bar icon each time it opens.
        Opened += (_, _) => PositionUnderTray();
        Deactivated += (_, _) =>
        {
            // Don't collapse the popover while a modal dialog (e.g. Add account)
            // is open, otherwise the dialog is orphaned and the window vanishes.
            if (!_dialogOpen) Hide();
        };

        // Press Esc to dismiss the popover (useful on Windows where there is no
        // title-bar minimise button on a borderless popover).
        KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape) Hide();
        };
    }

    private bool _dialogOpen;

    /// <summary>
    /// Places the popover horizontally centred under the menu-bar icon (which is
    /// where the mouse pointer is when the icon is clicked), and just below the
    /// menu bar — matching how native menu-bar popovers appear.
    /// </summary>
    private void PositionUnderTray()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null) return;

        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var area = screen.WorkingArea;
        var width = (int)(Width * scale);

        // Horizontal: centre under the pointer X (the clicked icon). Fall back to
        // the right side if the pointer can't be read.
        int anchorX = MacMouse.TryGetX(out var mouseX) ? mouseX : area.X + area.Width - width - 8;
        var x = anchorX - width / 2;

        // Clamp inside the screen.
        var minX = area.X + 4;
        var maxX = area.X + area.Width - width - 4;
        if (x < minX) x = minX;
        if (x > maxX) x = maxX;

        var y = area.Y + 4; // just under the menu bar
        Position = new PixelPoint(x, y);
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await Vm.CopyOtpAsync(clipboard.SetTextAsync);
    }

    private void OnAddManual(object? sender, RoutedEventArgs e) => Vm?.AddFromManualUri();

    private void OnToggleReveal(object? sender, RoutedEventArgs e) => Vm?.ToggleReveal();

    private void OnTogglePassword(object? sender, RoutedEventArgs e) => Vm?.TogglePassword();

    private async void OnShowDetails(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        _dialogOpen = true;
        try
        {
            var dialog = new Window
            {
                Title = "Account details",
                Width = 320,
                Height = 200,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brush.Parse("#2b2b2e"),
                Content = new TextBlock
                {
                    Text = Vm.CurrentAccountDetails(),
                    Margin = new Avalonia.Thickness(20),
                    Foreground = Avalonia.Media.Brushes.White,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
            };
            await dialog.ShowDialog(this);
        }
        finally { _dialogOpen = false; }
    }

    private void OnDeleteAccount(object? sender, RoutedEventArgs e) => Vm?.DeleteCurrentAccount();

    private void OnInstallUpdate(object? sender, RoutedEventArgs e) => Vm?.InstallUpdate();

    private async void OnCheckRequirements(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        _dialogOpen = true;
        try
        {
            var dialog = new Window
            {
                Title = "Requirements",
                Width = 440,
                Height = 320,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brush.Parse("#2b2b2e"),
                Content = new ScrollViewer
                {
                    Margin = new Avalonia.Thickness(20),
                    Content = new TextBlock
                    {
                        Text = Vm.PrerequisitesText(),
                        Foreground = Avalonia.Media.Brushes.White,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontFamily = new Avalonia.Media.FontFamily("Menlo, monospace"),
                        FontSize = 12,
                    },
                },
            };
            await dialog.ShowDialog(this);
        }
        finally { _dialogOpen = false; }
    }

    private void OnConnect(object? sender, RoutedEventArgs e) => Vm?.BeginConnect();

    private void OnQuit(object? sender, RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    // The "+" button: open the Add account dialog (QR image or manual entry).
    private async void OnChooseQr(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        _dialogOpen = true;
        try
        {
            var dialog = new AddAccountWindow();
            await dialog.ShowDialog(this);
            if (dialog.Result is { } entry)
                Vm.AddEntry(entry);
        }
        finally
        {
            _dialogOpen = false;
        }
    }
}
