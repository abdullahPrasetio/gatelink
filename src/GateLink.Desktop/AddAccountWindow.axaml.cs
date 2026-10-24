using Avalonia.Controls;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using GateLink.Core.Otp;
using GateLink.Core.Vault;

namespace GateLink.Desktop;

public partial class AddAccountWindow : Window
{
    /// <summary>The entry the user added, or null if they cancelled.</summary>
    public VaultEntry? Result { get; private set; }

    public AddAccountWindow() => AvaloniaXamlLoader.Load(this);

    // Resolve controls by name at runtime to avoid depending on the XAML name
    // generator (which clashed with manual fields across build environments).
    private TextBox Uri => this.FindControl<TextBox>("UriBox")!;
    private TextBox Label => this.FindControl<TextBox>("LabelBox")!;
    private TextBlock Message => this.FindControl<TextBlock>("MessageText")!;

    private async void OnChooseQr(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a QR image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("QR images")
                {
                    Patterns = new[]
                    {
                        "*.png", "*.jpg", "*.jpeg", "*.jpe", "*.gif", "*.bmp",
                        "*.webp", "*.tif", "*.tiff", "*.heic", "*.heif",
                    },
                    // Windows matches on MIME types for some dialogs; include them
                    // so JPEGs aren't filtered out.
                    MimeTypes = new[]
                    {
                        "image/png", "image/jpeg", "image/gif", "image/bmp",
                        "image/webp", "image/tiff", "image/heic", "image/*",
                    },
                    AppleUniformTypeIdentifiers = new[] { "public.image" },
                },
                // Escape hatch so a correctly-named file is never hidden.
                new FilePickerFileType("All files") { Patterns = new[] { "*" } },
            },
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var uri = QrDecoder.DecodeOtpAuthFile(path);
            Finish(VaultEntry.FromOtpAuth(uri) with { VpnUsername = uri.Account });
        }
        catch (Exception ex)
        {
            Message.Text = $"Could not read QR: {ex.Message}";
        }
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var text = Uri.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Message.Text = "Enter an otpauth URI or a Base32 secret.";
            return;
        }

        try
        {
            VaultEntry entry;
            if (text.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = OtpAuthUri.Parse(text);
                entry = VaultEntry.FromOtpAuth(uri) with { VpnUsername = uri.Account };
            }
            else
            {
                // Treat the input as a raw Base32 secret. Validate by decoding.
                _ = Base32.Decode(text);
                var label = string.IsNullOrWhiteSpace(Label.Text) ? "VPN" : Label.Text!.Trim();
                entry = new VaultEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Issuer = label,
                    Account = "account",
                    Secret = text.Replace(" ", "").ToUpperInvariant(),
                };
            }

            Finish(entry);
        }
        catch (Exception ex)
        {
            Message.Text = $"Could not add: {ex.Message}";
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void Finish(VaultEntry entry)
    {
        Result = entry;
        Close();
    }
}
