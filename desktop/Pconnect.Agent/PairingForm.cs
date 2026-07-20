using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Pconnect.Agent.Services;
using Pconnect.Agent.UI;
using QRCoder;

namespace Pconnect.Agent;

internal sealed class PairingForm : Form
{
    private readonly Label _codeLabel;
    private readonly Label _urlLabel;
    private readonly PictureBox _qrPictureBox;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AgentRuntime _runtime;

    public PairingForm(AgentRuntime runtime, string code)
    {
        _runtime = runtime;
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Pconnect Pairing";
        Width = 460;
        Height = 440;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.TextPrimary;

        var card = new ModernCard
        {
            Top = 15,
            Left = 15,
            Width = 415,
            Height = 370,
            Title = "Pair Mobile Device",
            Subtitle = "Enter this PIN code or scan the QR code on your phone.",
        };

        _codeLabel = new Label
        {
            Text = code,
            AutoSize = true,
            Font = new Font("Segoe UI", 28, FontStyle.Bold),
            ForeColor = ThemeColors.Primary,
            Left = 20,
            Top = 50,
        };

        _urlLabel = new Label
        {
            Text = FormatUrlHint(runtime),
            AutoSize = true,
            Font = ThemeColors.SmallFont,
            ForeColor = ThemeColors.TextSecondary,
            Left = 20,
            Top = 115,
            MaximumSize = new Size(375, 0),
        };

        _qrPictureBox = new PictureBox
        {
            Left = 20,
            Top = 165,
            Width = 170,
            Height = 170,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
        };
        UpdateQrCode(code);

        var copyButton = new ModernButton
        {
            Text = "Copy WebSocket URL",
            Style = ModernButtonStyle.Primary,
            Left = 210,
            Top = 165,
            Width = 185,
            Height = 36,
        };
        copyButton.Click += (_, _) =>
        {
            var url = runtime.GetLikelyWebSocketUrl();
            if (url is not null) Clipboard.SetText(url);
        };

        var copyPinButton = new ModernButton
        {
            Text = "Copy PIN",
            Style = ModernButtonStyle.Secondary,
            Left = 210,
            Top = 215,
            Width = 185,
            Height = 36,
        };
        copyPinButton.Click += (_, _) =>
        {
            Clipboard.SetText(_codeLabel.Text);
        };

        var closeButton = new ModernButton
        {
            Text = "Close",
            Style = ModernButtonStyle.Outline,
            Left = 210,
            Top = 265,
            Width = 185,
            Height = 36,
        };
        closeButton.Click += (_, _) => Close();

        card.Controls.Add(_codeLabel);
        card.Controls.Add(_urlLabel);
        card.Controls.Add(_qrPictureBox);
        card.Controls.Add(copyButton);
        card.Controls.Add(copyPinButton);
        card.Controls.Add(closeButton);

        Controls.Add(card);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshCode();
        _timer.Start();
    }

    public void SetCode(string code)
    {
        _codeLabel.Text = code;
        UpdateQrCode(code);
    }

    private static string FormatUrlHint(AgentRuntime runtime)
    {
        var url = runtime.GetLikelyWebSocketUrl();
        if (url is not null)
        {
            var all = runtime.GetLanIpv4Candidates();
            if (all.Count > 1)
            {
                return $"{url}  (also: {string.Join(", ", all.Skip(1))})";
            }

            return url;
        }

        return "Set PC IP manually on phone (no LAN IPv4 detected)";
    }

    private void UpdateQrCode(string code)
    {
        try
        {
            var url = _runtime.GetLikelyWebSocketUrl();
            if (url is null)
            {
                _qrPictureBox.Image?.Dispose();
                _qrPictureBox.Image = null;
                return;
            }

            var uri = new Uri(url);
            var qrData = JsonSerializer.Serialize(new
            {
                ip = uri.Host,
                port = uri.Port,
                wssPort = AgentRuntime.DefaultWssPort,
                pairingCode = code,
            });

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(qrData, QRCodeGenerator.ECCLevel.M);
            using var qrCode = new PngByteQRCode(qrCodeData);
            var pngBytes = qrCode.GetGraphic(4);

            using var ms = new MemoryStream(pngBytes);
            var oldImage = _qrPictureBox.Image;
            _qrPictureBox.Image = Image.FromStream(ms);
            oldImage?.Dispose();
        }
        catch
        {
            // QR fallback
        }
    }

    private void RefreshCode()
    {
        var current = _runtime.Pairing.CurrentCode;
        if (!string.Equals(_codeLabel.Text, current, StringComparison.Ordinal))
        {
            _codeLabel.Text = current;
            UpdateQrCode(current);
        }

        var hint = FormatUrlHint(_runtime);
        if (!string.Equals(_urlLabel.Text, hint, StringComparison.Ordinal))
        {
            _urlLabel.Text = hint;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _qrPictureBox.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
