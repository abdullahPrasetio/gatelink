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

    private TextBox UriBox => this.FindControl<TextBox>("UriBox")!;
    private TextBox LabelBox => this.FindControl<TextBox>("LabelBox")!;
    private TextBlock MessageText => this.FindControl<TextBlock>("MessageText")!;

    private async void OnChooseQr(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a QR image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp" },
                },
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
            MessageText.Text = $"Could not read QR: {ex.Message}";
        }
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        var text = UriBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageText.Text = "Enter an otpauth URI or a Base32 secret.";
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
                var label = string.IsNullOrWhiteSpace(LabelBox.Text) ? "VPN" : LabelBox.Text!.Trim();
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
            MessageText.Text = $"Could not add: {ex.Message}";
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void Finish(VaultEntry entry)
    {
        Result = entry;
        Close();
    }
}
