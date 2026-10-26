using GateLink.Core.Otp;
using SkiaSharp;
using Xunit;
using ZXing;
using ZXing.QrCode;
using ZXing.Common;

namespace GateLink.Core.Tests;

public class QrDecoderTests
{
    private const string SampleUri =
        "otpauth://totp/Example%20VPN:alice?secret=JBSWY3DPEHPK3PXP&issuer=Example%20VPN&digits=6&period=30&algorithm=SHA1";

    /// <summary>Renders text into a QR PNG using ZXing + SkiaSharp.</summary>
    private static byte[] MakeQrPng(string text, int size = 300)
    {
        var writer = new QRCodeWriter();
        var hints = new Dictionary<EncodeHintType, object>
        {
            [EncodeHintType.MARGIN] = 2,
            [EncodeHintType.CHARACTER_SET] = "UTF-8",
        };
        BitMatrix matrix = writer.encode(text, BarcodeFormat.QR_CODE, size, size, hints);

        using var bmp = new SKBitmap(matrix.Width, matrix.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        for (int y = 0; y < matrix.Height; y++)
            for (int x = 0; x < matrix.Width; x++)
                bmp.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void DecodeBytes_RoundTripsOtpAuthUri()
    {
        var png = MakeQrPng(SampleUri);

        var decoded = QrDecoder.DecodeBytes(png);

        Assert.Equal(SampleUri, decoded);
    }

    [Fact]
    public void DecodeBytes_ThenParse_YieldsWorkingGenerator()
    {
        var png = MakeQrPng(SampleUri);

        var text = QrDecoder.DecodeBytes(png);
        var parsed = OtpAuthUri.Parse(text!);
        var code = parsed.CreateGenerator().Generate();

        Assert.Equal("JBSWY3DPEHPK3PXP", parsed.Secret);
        Assert.Equal(6, code.Length);
    }

    [Fact]
    public void DecodeFile_RoundTrips()
    {
        var png = MakeQrPng(SampleUri);
        var path = Path.Combine(Path.GetTempPath(), $"gatelink-qr-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, png);
            var descriptor = QrDecoder.DecodeOtpAuthFile(path);
            Assert.Equal("alice", descriptor.Account);
            Assert.Equal("Example VPN", descriptor.Issuer);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
