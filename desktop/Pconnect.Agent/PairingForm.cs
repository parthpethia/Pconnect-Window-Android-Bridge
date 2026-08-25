using System.Drawing;
using System.Windows.Forms;
using Pconnect.Agent.Services;
using Pconnect.Agent.UI;

namespace Pconnect.Agent;

internal sealed class PairingForm : Form
{
    private readonly Label _codeLabel;
    private readonly Label _urlLabel;
    private readonly PictureBox _qrPictureBox;
    private readonly FlowLayoutPanel _ipChipsPanel;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly AgentRuntime _runtime;
    private string? _selectedIp;

    public PairingForm(AgentRuntime runtime, string code)
    {
        _runtime = runtime;
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Pconnect Security Pairing";
        Width = 480;
        Height = 470;
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
            Width = 435,
            Height = 400,
            Title = "Pair Mobile Device",
            Subtitle = "Scan QR code or enter the 6-digit PIN code on your phone.",
        };

        _codeLabel = new Label
        {
            Text = code,
            AutoSize = true,
            Font = new Font("Segoe UI", 30, FontStyle.Bold),
            ForeColor = ThemeColors.Primary,
            Left = 20,
            Top = 50,
        };

        _ipChipsPanel = new FlowLayoutPanel
        {
            Left = 20,
            Top = 108,
            Width = 395,
            Height = 32,
            WrapContents = false,
            AutoScroll = true,
        };

        _urlLabel = new Label
        {
            Text = FormatUrlHint(runtime, _selectedIp),
            AutoSize = true,
            Font = ThemeColors.SmallFont,
            ForeColor = ThemeColors.TextSecondary,
            Left = 20,
            Top = 145,
            MaximumSize = new Size(395, 0),
        };

        _qrPictureBox = new PictureBox
        {
            Left = 20,
            Top = 180,
            Width = 190,
            Height = 190,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = ThemeColors.Surface,
        };

        var copyButton = new ModernButton
        {
            Text = "Copy WS URL",
            Style = ModernButtonStyle.Primary,
            Left = 225,
            Top = 180,
            Width = 185,
            Height = 38,
        };
        copyButton.Click += (_, _) =>
        {
            var url = GetCurrentWebSocketUrl();
            if (url is not null)
            {
                Clipboard.SetText(url);
                MessageBox.Show($"Copied to clipboard:\n{url}", "Pconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        var copyPinButton = new ModernButton
        {
            Text = "Copy PIN",
            Style = ModernButtonStyle.Secondary,
            Left = 225,
            Top = 230,
            Width = 185,
            Height = 38,
        };
        copyPinButton.Click += (_, _) =>
        {
            Clipboard.SetText(_codeLabel.Text);
        };

        var regenPinBtn = new ModernButton
        {
            Text = "Regenerate PIN",
            Style = ModernButtonStyle.Secondary,
            Left = 225,
            Top = 280,
            Width = 185,
            Height = 38,
        };
        regenPinBtn.Click += (_, _) =>
        {
            _runtime.Pairing.RotateCode();
            RefreshCode();
        };

        var closeButton = new ModernButton
        {
            Text = "Close",
            Style = ModernButtonStyle.Outline,
            Left = 225,
            Top = 330,
            Width = 185,
            Height = 38,
        };
        closeButton.Click += (_, _) => Close();

        card.Controls.Add(_codeLabel);
        card.Controls.Add(_ipChipsPanel);
        card.Controls.Add(_urlLabel);
        card.Controls.Add(_qrPictureBox);
        card.Controls.Add(copyButton);
        card.Controls.Add(copyPinButton);
        card.Controls.Add(regenPinBtn);
        card.Controls.Add(closeButton);

        Controls.Add(card);

        PopulateIpChips();
        UpdateQrCode(code);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => RefreshCode();
        _timer.Start();
    }

    public void SetCode(string code)
    {
        _codeLabel.Text = code;
        UpdateQrCode(code);
    }

    private string? GetCurrentWebSocketUrl()
    {
        var ips = _runtime.GetLanIpv4Candidates();
        if (ips.Count == 0) return null;
        var ip = (!string.IsNullOrEmpty(_selectedIp) && ips.Contains(_selectedIp)) ? _selectedIp : ips[0];
        return $"ws://{ip}:{AgentRuntime.DefaultWsPort}/ws";
    }

    private void PopulateIpChips()
    {
        _ipChipsPanel.Controls.Clear();
        var ips = _runtime.GetLanIpv4Candidates();
        if (ips.Count == 0) return;

        if (string.IsNullOrEmpty(_selectedIp) || !ips.Contains(_selectedIp))
        {
            _selectedIp = ips[0];
        }

        foreach (var ip in ips)
        {
            bool isSelected = string.Equals(ip, _selectedIp, StringComparison.Ordinal);
            var chip = new ModernButton
            {
                Text = ip,
                Style = isSelected ? ModernButtonStyle.Primary : ModernButtonStyle.Secondary,
                Height = 26,
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0),
            };
            var targetIp = ip;
            chip.Click += (_, _) =>
            {
                _selectedIp = targetIp;
                PopulateIpChips();
                UpdateQrCode(_codeLabel.Text);
                _urlLabel.Text = FormatUrlHint(_runtime, _selectedIp);
            };
            _ipChipsPanel.Controls.Add(chip);
        }
    }

    private static string FormatUrlHint(AgentRuntime runtime, string? selectedIp)
    {
        var ips = runtime.GetLanIpv4Candidates();
        if (ips.Count > 0)
        {
            var activeIp = (!string.IsNullOrEmpty(selectedIp) && ips.Contains(selectedIp)) ? selectedIp : ips[0];
            var url = $"ws://{activeIp}:{AgentRuntime.DefaultWsPort}/ws";
            if (ips.Count > 1)
            {
                return $"{url}  (also: {string.Join(", ", ips.Where(i => i != activeIp))})";
            }
            return url;
        }

        return "Set PC IP manually on phone (no LAN IPv4 detected)";
    }

    private void UpdateQrCode(string code)
    {
        try
        {
            if (!_runtime.IsServerRunning)
            {
                var oldImg = _qrPictureBox.Image;
                _qrPictureBox.Image = QrCodeHelper.GenerateFallbackImage("Server Stopped", "Start server in Dashboard to display QR code", 190);
                oldImg?.Dispose();
                return;
            }

            var ips = _runtime.GetLanIpv4Candidates();
            if (ips.Count == 0)
            {
                var oldImg = _qrPictureBox.Image;
                _qrPictureBox.Image = QrCodeHelper.GenerateFallbackImage("No LAN IPv4", "Connect PC to Wi-Fi or Ethernet network", 190);
                oldImg?.Dispose();
                return;
            }

            var activeIp = (!string.IsNullOrEmpty(_selectedIp) && ips.Contains(_selectedIp)) ? _selectedIp : ips[0];
            var oldImage = _qrPictureBox.Image;
            _qrPictureBox.Image = QrCodeHelper.GenerateQrImage(activeIp, AgentRuntime.DefaultWsPort, AgentRuntime.DefaultWssPort, code, 190);
            oldImage?.Dispose();
        }
        catch
        {
            var oldImg = _qrPictureBox.Image;
            _qrPictureBox.Image = QrCodeHelper.GenerateFallbackImage("QR Render Error", "Use manual IP connection on mobile app", 190);
            oldImg?.Dispose();
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

        PopulateIpChips();
        var hint = FormatUrlHint(_runtime, _selectedIp);
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

