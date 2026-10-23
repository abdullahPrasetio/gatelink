using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace GateLink.Core.Otp;

/// <summary>
/// Decodes a QR code from an image file or raw bytes into its text payload
/// (for 2FA setup, an <c>otpauth://</c> URI). Uses SkiaSharp to load the image
/// and ZXing.Net to decode — both cross-platform, so this works on Windows and
/// macOS. On the desktop, "scan" means picking an image file or pasting a
/// screenshot rather than using a camera.
/// </summary>
public static class QrDecoder
{
    /// <summary>Decodes a QR code from an image file. Returns null if none found.</summary>
    public static string? DecodeFile(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("QR image not found.", imagePath);

        using var stream = File.OpenRead(imagePath);
        return DecodeStream(stream);
    }

    /// <summary>Decodes a QR code from image bytes (e.g. clipboard/screenshot).</summary>
    public static string? DecodeBytes(byte[] imageBytes)
    {
        using var ms = new MemoryStream(imageBytes);
        return DecodeStream(ms);
    }

    /// <summary>Decodes a QR code directly as an otpauth URI descriptor.</summary>
    public static OtpAuthUri DecodeOtpAuthFile(string imagePath)
    {
        var text = DecodeFile(imagePath)
                   ?? throw new InvalidOperationException("No QR code found in the image.");
        return OtpAuthUri.Parse(text);
    }

    private static string? DecodeStream(Stream stream)
    {
        using var bitmap = SKBitmap.Decode(stream);
        if (bitmap is null)
            throw new InvalidOperationException("Unsupported or corrupt image.");

        // Normalise to 8-bit RGBA so the luminance source has a known layout.
        using var surface = new SKBitmap(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bitmap.CopyTo(surface, SKColorType.Rgba8888);

        var rgbaBytes = surface.Bytes;
        var luminance = new RGBLuminanceSource(
            rgbaBytes, surface.Width, surface.Height, RGBLuminanceSource.BitmapFormat.RGBA32);

        var reader = new QRCodeReader();
        var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true };

        var binarizer = new HybridBinarizer(luminance);
        var binaryBitmap = new BinaryBitmap(binarizer);

        var result = reader.decode(binaryBitmap, hints);
        return result?.Text;
    }
}
