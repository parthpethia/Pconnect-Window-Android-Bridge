using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Pconnect.Agent.Services;
using QRCoder;

namespace Pconnect.Agent.UI;

internal static class QrCodeHelper
{
    public static Bitmap GenerateQrImage(string ip, int port, int wssPort, string pairingCode, int size = 220)
    {
        var qrData = JsonSerializer.Serialize(new
        {
            ip = ip,
            port = port,
            wssPort = wssPort,
            pairingCode = pairingCode,
        });

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(qrData, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        var pngBytes = qrCode.GetGraphic(5);

        using var ms = new MemoryStream(pngBytes);
        using var tempImage = Image.FromStream(ms);

        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.Clear(Color.White);

            int margin = 12;
            g.DrawImage(tempImage, new Rectangle(margin, margin, size - (2 * margin), size - (2 * margin)));

            using var pen = new Pen(ThemeColors.Primary, 2.5f);
            g.DrawRectangle(pen, 1, 1, size - 3, size - 3);
        }

        return bmp;
    }

    public static Bitmap GenerateFallbackImage(string title, string subtitle, int size = 220)
    {
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(ThemeColors.Surface);

            using var borderPen = new Pen(ThemeColors.CardBorder, 1.5f);
            g.DrawRectangle(borderPen, 1, 1, size - 3, size - 3);

            var titleRect = new Rectangle(10, (size / 2) - 28, size - 20, 26);
            TextRenderer.DrawText(g, title, ThemeColors.BoldBodyFont, titleRect, ThemeColors.Warning, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var subRect = new Rectangle(12, (size / 2) + 2, size - 24, 50);
            TextRenderer.DrawText(g, subtitle, ThemeColors.SmallFont, subRect, ThemeColors.TextSecondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        }

        return bmp;
    }
}
