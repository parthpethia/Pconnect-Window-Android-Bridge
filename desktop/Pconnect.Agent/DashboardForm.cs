using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using System.Windows.Forms;
using Pconnect.Agent.Services;
using Pconnect.Agent.UI;
using QRCoder;

namespace Pconnect.Agent;

internal sealed class DashboardForm : Form
{
    private readonly AgentRuntime _runtime;
    private readonly SystemMetricsService _metricsService = new();
    private readonly System.Windows.Forms.Timer _refreshTimer;

    // UI Controls
    private readonly ModernTabControl _tabControl;
    private readonly Panel _contentPanel;

    // Overview Tab Controls
    private Panel? _overviewPage;
    private Label? _serverStatusValue;
    private Label? _connectedDeviceValue;
    private FlowLayoutPanel? _ipChipsPanel;
    private ModernButton? _toggleServerButton;

    // Windows Control Tab Controls
    private Panel? _controlPage;
    private ModernSlider? _volumeSlider;
    private ModernSlider? _brightnessSlider;

    // Pairing Tab Controls
    private Panel? _pairingPage;
    private Label? _pinCodeLabel;
    private ModernProgressBar? _pinCountdownBar;
    private PictureBox? _qrPictureBox;
    private Label? _qrUrlLabel;

    // System Metrics Tab Controls
    private Panel? _metricsPage;
    private ModernProgressBar? _cpuProgressBar;
    private ModernProgressBar? _ramProgressBar;
    private Label? _uptimeLabel;
    private Label? _processCountLabel;

    // Audit Logs Tab Controls
    private Panel? _auditPage;
    private ListView? _auditListView;

    internal bool AllowClose { get; set; }

    public DashboardForm(AgentRuntime runtime)
    {
        _runtime = runtime;
        AutoScaleMode = AutoScaleMode.Dpi;

        // Form properties
        Text = _runtime.SafeStartup.IsSafeMode ? "Pconnect Agent (Safe Mode)" : "Pconnect Agent";
        Width = 780;
        Height = 640;
        MinimumSize = new Size(780, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.TextPrimary;
        Font = ThemeColors.BodyFont;
        Icon = SystemIcons.Application;

        // Top Header Panel
        var headerPanel = CreateHeaderPanel();
        Controls.Add(headerPanel);

        // Tab Bar Navigation
        _tabControl = new ModernTabControl
        {
            Dock = DockStyle.Top,
            Height = 44,
        };
        _tabControl.Tabs.AddRange(new[] { "Overview", "Windows Control", "Pairing & QR", "System Stats", "Audit Logs" });
        _tabControl.SelectedIndexChanged += (_, _) => SwitchTab(_tabControl.SelectedIndex);
        Controls.Add(_tabControl);

        // Main Content Area
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeColors.Background,
            Padding = new Padding(16, 12, 16, 12),
        };
        Controls.Add(_contentPanel);

        // Build Pages
        BuildOverviewPage();
        BuildControlPage();
        BuildPairingPage();
        BuildMetricsPage();
        BuildAuditPage();

        // Default tab: Overview
        SwitchTab(0);

        // Handle Close to Tray
        FormClosing += (_, e) =>
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };

        // Runtime State Updates
        _runtime.StateChanged += (_, _) => PostUpdateUi();
        Shown += (_, _) => UpdateUi();

        // 1-second metric timer
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) => OnTimerTick();
        _refreshTimer.Start();
    }

    private Panel CreateHeaderPanel()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = ThemeColors.Surface,
            Padding = new Padding(20, 12, 20, 12),
        };

        var titleLabel = new Label
        {
            Text = "Pconnect Desktop Agent",
            Font = ThemeColors.HeaderFont,
            ForeColor = ThemeColors.TextPrimary,
            AutoSize = true,
            Left = 20,
            Top = 14,
        };

        var subtitleLabel = new Label
        {
            Text = "LAN Remote Control & Hardware Bridge for Windows",
            Font = ThemeColors.SubtitleFont,
            ForeColor = ThemeColors.TextSecondary,
            AutoSize = true,
            Left = 20,
            Top = 42,
        };

        bool isElevated;
        using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            isElevated = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }

        if (!isElevated)
        {
            var relaunchBtn = new ModernButton
            {
                Text = "Relaunch as Admin",
                Style = ModernButtonStyle.Primary,
                Width = 150,
                Height = 34,
                Top = 19,
            };
            relaunchBtn.Left = header.Width - relaunchBtn.Width - 20;
            relaunchBtn.Click += (_, _) => RelaunchAsAdmin();
            header.Controls.Add(relaunchBtn);
            header.Resize += (_, _) => { relaunchBtn.Left = header.Width - relaunchBtn.Width - 20; };
        }
        else
        {
            var badgeLabel = new Label
            {
                Text = "Elevated (Admin)",
                Font = ThemeColors.SmallFont,
                ForeColor = ThemeColors.Success,
                AutoSize = true,
                Top = 26,
            };
            badgeLabel.Left = header.Width - badgeLabel.Width - 24;
            header.Resize += (_, _) => { badgeLabel.Left = header.Width - badgeLabel.Width - 24; };
            header.Controls.Add(badgeLabel);
        }

        header.Controls.Add(titleLabel);
        header.Controls.Add(subtitleLabel);

        return header;
    }

    private static void RelaunchAsAdmin()
    {
        AdminRelaunchHelper.RelaunchAsAdmin();
    }

    private void SwitchTab(int index)
    {
        _contentPanel.Controls.Clear();
        switch (index)
        {
            case 0:
                if (_overviewPage != null) _contentPanel.Controls.Add(_overviewPage);
                break;
            case 1:
                if (_controlPage != null) _contentPanel.Controls.Add(_controlPage);
                break;
            case 2:
                if (_pairingPage != null) _contentPanel.Controls.Add(_pairingPage);
                RefreshPairingCode();
                break;
            case 3:
                if (_metricsPage != null) _contentPanel.Controls.Add(_metricsPage);
                UpdateMetrics();
                break;
            case 4:
                if (_auditPage != null) _contentPanel.Controls.Add(_auditPage);
                RefreshAuditLogs();
                break;
        }
    }

    #region Tab 0: Overview Page
    private void BuildOverviewPage()
    {
        _overviewPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int top = 8;
        int cardWidth = 726;

        // Admin Warning Banner if non-elevated
        using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
        {
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                var adminCard = new ModernCard
                {
                    Top = top,
                    Left = 0,
                    Width = cardWidth,
                    Height = 60,
                    CardBgColor = Color.FromArgb(45, 25, 25),
                    BorderColor = ThemeColors.Danger,
                    Title = "Running as Standard User",
                    Subtitle = "Mouse & keyboard input may be blocked when controlling elevated apps.",
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                };

                var adminCardBtn = new ModernButton
                {
                    Text = "Grant Admin Access",
                    Style = ModernButtonStyle.Danger,
                    Left = cardWidth - 170,
                    Top = 13,
                    Width = 155,
                    Height = 34,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                };
                adminCardBtn.Click += (_, _) => RelaunchAsAdmin();
                adminCard.Controls.Add(adminCardBtn);

                _overviewPage.Controls.Add(adminCard);
                top += 70;
            }
        }

        // Safe Mode Banner
        if (_runtime.SafeStartup.IsSafeMode)
        {
            var safeCard = new ModernCard
            {
                Top = top,
                Left = 0,
                Width = cardWidth,
                Height = 60,
                CardBgColor = Color.FromArgb(45, 35, 20),
                BorderColor = ThemeColors.Warning,
                Title = "Safe Mode Active",
                Subtitle = string.Join("; ", _runtime.SafeStartup.Reasons),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            _overviewPage.Controls.Add(safeCard);
            top += 70;
        }

        // Server Status Card
        var statusCard = new ModernCard
        {
            Top = top,
            Left = 0,
            Width = cardWidth,
            Height = 165,
            Title = "Server Status & Network Info",
            Subtitle = "Local network WebSocket server listening for mobile remote connections",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var serverLabel = new Label { Text = "Server Status:", Left = 20, Top = 58, AutoSize = true, ForeColor = ThemeColors.TextSecondary };
        _serverStatusValue = new Label { Text = "Active", Left = 150, Top = 58, AutoSize = true, Font = ThemeColors.BoldBodyFont, ForeColor = ThemeColors.Success };

        var deviceLabel = new Label { Text = "Connected Device:", Left = 20, Top = 90, AutoSize = true, ForeColor = ThemeColors.TextSecondary };
        _connectedDeviceValue = new Label { Text = "None", Left = 150, Top = 90, AutoSize = true, Font = ThemeColors.BoldBodyFont, ForeColor = ThemeColors.TextPrimary };

        var ipLabel = new Label { Text = "LAN IP Addresses:", Left = 20, Top = 122, AutoSize = true, ForeColor = ThemeColors.TextSecondary };
        _ipChipsPanel = new FlowLayoutPanel
        {
            Left = 150,
            Top = 118,
            Width = cardWidth - 330,
            Height = 36,
            AutoScroll = true,
            WrapContents = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _toggleServerButton = new ModernButton
        {
            Text = "Stop Server",
            Style = ModernButtonStyle.Danger,
            Left = cardWidth - 170,
            Top = 60,
            Width = 150,
            Height = 38,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _toggleServerButton.Click += async (_, _) => await ToggleServerAsync();

        statusCard.Controls.Add(serverLabel);
        statusCard.Controls.Add(_serverStatusValue);
        statusCard.Controls.Add(deviceLabel);
        statusCard.Controls.Add(_connectedDeviceValue);
        statusCard.Controls.Add(ipLabel);
        statusCard.Controls.Add(_ipChipsPanel);
        statusCard.Controls.Add(_toggleServerButton);

        _overviewPage.Controls.Add(statusCard);
        top += 177;

        // Quick Setup Card
        var quickCard = new ModernCard
        {
            Top = top,
            Left = 0,
            Width = cardWidth,
            Height = 118,
            Title = "Quick Setup & Mobile Pairing",
            Subtitle = "Connect your mobile phone to control volume, input, media, and desktop view.",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var pairButton = new ModernButton
        {
            Text = "Show Pairing PIN & QR",
            Style = ModernButtonStyle.Primary,
            Left = 20,
            Top = 60,
            Width = 200,
            Height = 38,
        };
        pairButton.Click += (_, _) => _tabControl.SelectedIndex = 2;

        var copyWsButton = new ModernButton
        {
            Text = "Copy WebSocket URL",
            Style = ModernButtonStyle.Secondary,
            Left = 235,
            Top = 60,
            Width = 190,
            Height = 38,
        };
        copyWsButton.Click += (_, _) => CopyWebSocketUrl();

        quickCard.Controls.Add(pairButton);
        quickCard.Controls.Add(copyWsButton);

        _overviewPage.Controls.Add(quickCard);
    }
    #endregion

    #region Tab 1: Windows Control Center Page
    private void BuildControlPage()
    {
        _controlPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        int top = 8;
        int cardWidth = 726;

        // Audio & Display Control Card
        var audioDisplayCard = new ModernCard
        {
            Top = top,
            Left = 0,
            Width = cardWidth,
            Height = 150,
            Title = "Audio & Display Controls",
            Subtitle = "Adjust Windows master volume levels and screen brightness",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var volLabel = new Label { Text = "Master Volume", Left = 20, Top = 52, AutoSize = true, Font = ThemeColors.BoldBodyFont };
        _volumeSlider = new ModernSlider
        {
            Left = 145,
            Top = 46,
            Width = cardWidth - 275,
            Value = 50,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _volumeSlider.ValueChanged += (_, _) => _runtime.Pc.SetVolume(_volumeSlider.Value);

        var muteButton = new ModernButton
        {
            Text = "Mute",
            Style = ModernButtonStyle.Secondary,
            Left = cardWidth - 115,
            Top = 46,
            Width = 95,
            Height = 32,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        muteButton.Click += (_, _) => _runtime.Pc.ToggleMuteAudio();

        var brLabel = new Label { Text = "Brightness", Left = 20, Top = 98, AutoSize = true, Font = ThemeColors.BoldBodyFont };
        _brightnessSlider = new ModernSlider
        {
            Left = 145,
            Top = 94,
            Width = cardWidth - 165,
            Value = 80,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _brightnessSlider.ValueChanged += (_, _) => _runtime.Pc.SetBrightness(_brightnessSlider.Value);

        audioDisplayCard.Controls.Add(volLabel);
        audioDisplayCard.Controls.Add(_volumeSlider);
        audioDisplayCard.Controls.Add(muteButton);
        audioDisplayCard.Controls.Add(brLabel);
        audioDisplayCard.Controls.Add(_brightnessSlider);

        _controlPage.Controls.Add(audioDisplayCard);
        top += 162;

        // Windows Power & Security Actions Card
        var powerCard = new ModernCard
        {
            Top = top,
            Left = 0,
            Width = cardWidth,
            Height = 115,
            Title = "Windows Power & Security Actions",
            Subtitle = "Instant session state and power management",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var lockBtn = new ModernButton { Text = "Lock PC", Style = ModernButtonStyle.Secondary, Top = 58, Height = 38 };
        lockBtn.Click += (_, _) => _runtime.Pc.Lock();

        var sleepBtn = new ModernButton { Text = "Sleep PC", Style = ModernButtonStyle.Secondary, Top = 58, Height = 38 };
        sleepBtn.Click += (_, _) => _runtime.Pc.Sleep();

        var restartBtn = new ModernButton { Text = "Restart PC", Style = ModernButtonStyle.Outline, Top = 58, Height = 38 };
        restartBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("Are you sure you want to restart your PC?", "Pconnect", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _runtime.Pc.Restart();
        };

        var shutdownBtn = new ModernButton { Text = "Shutdown PC", Style = ModernButtonStyle.Danger, Top = 58, Height = 38 };
        shutdownBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("Are you sure you want to shutdown your PC?", "Pconnect", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                _runtime.Pc.Shutdown();
        };

        powerCard.Controls.Add(lockBtn);
        powerCard.Controls.Add(sleepBtn);
        powerCard.Controls.Add(restartBtn);
        powerCard.Controls.Add(shutdownBtn);

        void LayoutPowerCard()
        {
            int pGap = 14;
            int pWidth = Math.Max(80, (powerCard.Width - 40 - (3 * pGap)) / 4);
            lockBtn.Left = 20; lockBtn.Width = pWidth;
            sleepBtn.Left = 20 + pWidth + pGap; sleepBtn.Width = pWidth;
            restartBtn.Left = 20 + (2 * (pWidth + pGap)); restartBtn.Width = pWidth;
            shutdownBtn.Left = 20 + (3 * (pWidth + pGap)); shutdownBtn.Width = pWidth;
        }

        powerCard.Resize += (_, _) => LayoutPowerCard();
        LayoutPowerCard();

        _controlPage.Controls.Add(powerCard);
        top += 127;

        // Windows Shortcuts & App Launcher Card
        var appsCard = new ModernCard
        {
            Top = top,
            Left = 0,
            Width = cardWidth,
            Height = 115,
            Title = "Quick Windows Shortcuts & Launchers",
            Subtitle = "Launch essential desktop utilities with one click",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var taskmgrBtn = new ModernButton { Text = "Task Manager", Style = ModernButtonStyle.Secondary, Top = 58, Height = 38 };
        taskmgrBtn.Click += (_, _) => _runtime.Pc.OpenTaskManager();

        var desktopBtn = new ModernButton { Text = "Show Desktop", Style = ModernButtonStyle.Secondary, Top = 58, Height = 38 };
        desktopBtn.Click += (_, _) => _runtime.Pc.ShowDesktop();

        var taskviewBtn = new ModernButton { Text = "Task View", Style = ModernButtonStyle.Secondary, Top = 58, Height = 38 };
        taskviewBtn.Click += (_, _) => _runtime.Pc.TaskView();

        var cmdBtn = new ModernButton { Text = "CMD", Style = ModernButtonStyle.Secondary, Top = 58, Height = 38 };
        cmdBtn.Click += (_, _) => _runtime.Pc.Launch("cmd.exe", null);

        var explorerBtn = new ModernButton { Text = "Explorer", Style = ModernButtonStyle.Secondary, Top = 58, Height = 38 };
        explorerBtn.Click += (_, _) => _runtime.Pc.Launch("explorer.exe", null);

        appsCard.Controls.Add(taskmgrBtn);
        appsCard.Controls.Add(desktopBtn);
        appsCard.Controls.Add(taskviewBtn);
        appsCard.Controls.Add(cmdBtn);
        appsCard.Controls.Add(explorerBtn);

        void LayoutAppsCard()
        {
            int aGap = 12;
            int aWidth = Math.Max(70, (appsCard.Width - 40 - (4 * aGap)) / 5);
            taskmgrBtn.Left = 20; taskmgrBtn.Width = aWidth;
            desktopBtn.Left = 20 + aWidth + aGap; desktopBtn.Width = aWidth;
            taskviewBtn.Left = 20 + (2 * (aWidth + aGap)); taskviewBtn.Width = aWidth;
            cmdBtn.Left = 20 + (3 * (aWidth + aGap)); cmdBtn.Width = aWidth;
            explorerBtn.Left = 20 + (4 * (aWidth + aGap)); explorerBtn.Width = aWidth;
        }

        appsCard.Resize += (_, _) => LayoutAppsCard();
        LayoutAppsCard();

        _controlPage.Controls.Add(appsCard);
    }
    #endregion

    #region Tab 2: Pairing & QR Code Page
    private void BuildPairingPage()
    {
        _pairingPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int cardWidth = 726;

        var qrCard = new ModernCard
        {
            Top = 8,
            Left = 0,
            Width = cardWidth,
            Height = 370,
            Title = "Device Pairing & QR Code",
            Subtitle = "Scan with Pconnect Mobile App or enter the rotating 6-digit Security PIN code.",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        var pinTitle = new Label
        {
            Text = "Security Pairing PIN",
            Font = ThemeColors.SubtitleFont,
            ForeColor = ThemeColors.TextSecondary,
            Left = 25,
            Top = 55,
            AutoSize = true,
        };

        _pinCodeLabel = new Label
        {
            Text = "------",
            Font = new Font("Segoe UI", 36, FontStyle.Bold),
            ForeColor = ThemeColors.Primary,
            Left = 25,
            Top = 75,
            AutoSize = true,
        };

        _pinCountdownBar = new ModernProgressBar
        {
            Left = 25,
            Top = 140,
            Width = 330,
            Label = "PIN Rotation Countdown",
            Value = 100,
        };

        _qrUrlLabel = new Label
        {
            Text = "WebSocket URL: ws://...",
            Font = ThemeColors.BodyFont,
            ForeColor = ThemeColors.TextSecondary,
            Left = 25,
            Top = 188,
            MaximumSize = new Size(400, 0),
            AutoSize = true,
        };

        _qrPictureBox = new PictureBox
        {
            Left = cardWidth - 255,
            Top = 55,
            Width = 230,
            Height = 230,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        var copyUrlBtn = new ModernButton
        {
            Text = "Copy WebSocket URL",
            Style = ModernButtonStyle.Primary,
            Left = 25,
            Top = 235,
            Width = 170,
            Height = 38,
        };
        copyUrlBtn.Click += (_, _) => CopyWebSocketUrl();

        var copyPinBtn = new ModernButton
        {
            Text = "Copy PIN",
            Style = ModernButtonStyle.Secondary,
            Left = 205,
            Top = 235,
            Width = 100,
            Height = 38,
        };
        copyPinBtn.Click += (_, _) =>
        {
            var code = _runtime.Pairing.CurrentCode;
            if (!string.IsNullOrEmpty(code)) Clipboard.SetText(code);
        };

        var regenPinBtn = new ModernButton
        {
            Text = "Regen PIN",
            Style = ModernButtonStyle.Outline,
            Left = 315,
            Top = 235,
            Width = 110,
            Height = 38,
        };
        regenPinBtn.Click += (_, _) =>
        {
            _runtime.Pairing.RotateCode();
            RefreshPairingCode();
        };

        qrCard.Controls.Add(pinTitle);
        qrCard.Controls.Add(_pinCodeLabel);
        qrCard.Controls.Add(_pinCountdownBar);
        qrCard.Controls.Add(_qrUrlLabel);
        qrCard.Controls.Add(_qrPictureBox);
        qrCard.Controls.Add(copyUrlBtn);
        qrCard.Controls.Add(copyPinBtn);
        qrCard.Controls.Add(regenPinBtn);

        _pairingPage.Controls.Add(qrCard);
    }

    private void RefreshPairingCode()
    {
        var code = _runtime.Pairing.CurrentCode;
        if (_pinCodeLabel != null) _pinCodeLabel.Text = code;
        if (_qrUrlLabel != null) _qrUrlLabel.Text = $"WebSocket URL: {_runtime.GetLikelyWebSocketUrl() ?? "Unavailable"}";

        if (_pinCountdownBar != null)
        {
            var elapsedSec = (DateTime.UtcNow - _runtime.Pairing.LastRotatedUtc).TotalSeconds;
            var remainingSec = Math.Max(0, 300 - (int)elapsedSec);
            var pct = Math.Clamp((int)((remainingSec / 300.0) * 100), 0, 100);
            _pinCountdownBar.Value = pct;
            _pinCountdownBar.Label = $"PIN Rotation Countdown ({remainingSec}s remaining)";
        }

        if (_qrPictureBox != null)
        {
            try
            {
                var url = _runtime.GetLikelyWebSocketUrl();
                if (url == null)
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
                var pngBytes = qrCode.GetGraphic(5);

                using var ms = new MemoryStream(pngBytes);
                var oldImage = _qrPictureBox.Image;
                _qrPictureBox.Image = Image.FromStream(ms);
                oldImage?.Dispose();
            }
            catch
            {
                // QR generation fallback
            }
        }
    }

    private void UpdateUi()
    {
        if (IsDisposed) return;

        bool running = _runtime.IsServerRunning;
        if (_serverStatusValue != null)
        {
            _serverStatusValue.Text = running ? "Active (Listening)" : "Stopped";
            _serverStatusValue.ForeColor = running ? ThemeColors.Success : ThemeColors.Danger;
        }

        if (_toggleServerButton != null)
        {
            _toggleServerButton.Text = running ? "Stop Server" : "Start Server";
            _toggleServerButton.Style = running ? ModernButtonStyle.Danger : ModernButtonStyle.Success;
        }

        if (_connectedDeviceValue != null)
        {
            _connectedDeviceValue.Text = _runtime.ConnectedDeviceDisplay;
        }

        if (_ipChipsPanel != null)
        {
            _ipChipsPanel.Controls.Clear();
            var ips = _runtime.GetLanIpv4Candidates();
            if (ips.Count == 0)
            {
                var noIpLabel = new Label
                {
                    Text = "No LAN IPv4 detected",
                    ForeColor = ThemeColors.TextMuted,
                    AutoSize = true,
                    Margin = new Padding(0, 4, 0, 0),
                };
                _ipChipsPanel.Controls.Add(noIpLabel);
            }
            else
            {
                foreach (var ip in ips)
                {
                    var chip = new ModernButton
                    {
                        Text = ip,
                        Style = ModernButtonStyle.Secondary,
                        Height = 28,
                        AutoSize = true,
                        Margin = new Padding(0, 0, 6, 0),
                    };
                    var targetIp = ip;
                    chip.Click += (_, _) =>
                    {
                        Clipboard.SetText(targetIp);
                        MessageBox.Show($"Copied {targetIp} to clipboard!", "Pconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    };
                    _ipChipsPanel.Controls.Add(chip);
                }
            }
        }
    }

    private void CopyWebSocketUrl()
    {
        var url = _runtime.GetLikelyWebSocketUrl();
        if (url == null)
        {
            MessageBox.Show("Could not determine an IP address.", "Pconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Clipboard.SetText(url);
        MessageBox.Show($"Copied to clipboard:\n{url}", "Pconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    #endregion

    #region Tab 3: System Performance Page
    private void BuildMetricsPage()
    {
        _metricsPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int cardWidth = 726;

        var statsCard = new ModernCard
        {
            Top = 8,
            Left = 0,
            Width = cardWidth,
            Height = 235,
            Title = "Real-time Hardware Resource Usage",
            Subtitle = "System telemetry monitored locally and streamable to paired mobile apps",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _cpuProgressBar = new ModernProgressBar
        {
            Left = 20,
            Top = 55,
            Width = cardWidth - 40,
            Label = "Processor (CPU) Usage",
            Value = 0,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _ramProgressBar = new ModernProgressBar
        {
            Left = 20,
            Top = 118,
            Width = cardWidth - 40,
            Label = "Memory (RAM) Usage",
            Value = 0,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _uptimeLabel = new Label
        {
            Text = "System Uptime: 0h 0m 0s",
            Font = ThemeColors.BodyFont,
            ForeColor = ThemeColors.TextSecondary,
            Left = 20,
            Top = 188,
            AutoSize = true,
        };

        _processCountLabel = new Label
        {
            Text = "Active Processes: 0",
            Font = ThemeColors.BodyFont,
            ForeColor = ThemeColors.TextSecondary,
            Left = cardWidth - 200,
            Top = 188,
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        statsCard.Controls.Add(_cpuProgressBar);
        statsCard.Controls.Add(_ramProgressBar);
        statsCard.Controls.Add(_uptimeLabel);
        statsCard.Controls.Add(_processCountLabel);

        _metricsPage.Controls.Add(statsCard);
    }

    private void UpdateMetrics()
    {
        var snapshot = _metricsService.GetSnapshot();
        if (_cpuProgressBar != null) _cpuProgressBar.Value = snapshot.CpuPercent;
        if (_ramProgressBar != null)
        {
            _ramProgressBar.Value = snapshot.RamPercent;
            _ramProgressBar.Label = $"Memory (RAM) Usage ({snapshot.UsedRamMb} MB / {snapshot.TotalRamMb} MB)";
        }

        if (_uptimeLabel != null)
        {
            var u = snapshot.Uptime;
            _uptimeLabel.Text = $"System Uptime: {(int)u.TotalHours}h {u.Minutes}m {u.Seconds}s";
        }

        if (_processCountLabel != null)
        {
            _processCountLabel.Text = $"Active Processes: {snapshot.ProcessCount}";
        }
    }
    #endregion

    #region Tab 4: Audit Logs Page
    private void BuildAuditPage()
    {
        _auditPage = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        int cardWidth = 726;

        var auditCard = new ModernCard
        {
            Top = 8,
            Left = 0,
            Width = cardWidth,
            Height = 380,
            Title = "Security Audit Log",
            Subtitle = "Historical record of incoming remote commands and session actions",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };

        _auditListView = new ListView
        {
            Left = 20,
            Top = 55,
            Width = cardWidth - 40,
            Height = 300,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };

        int colWidth = (cardWidth - 45) / 4;
        _auditListView.Columns.Add("Timestamp", colWidth);
        _auditListView.Columns.Add("Device", colWidth);
        _auditListView.Columns.Add("Command / Event", colWidth * 2);

        auditCard.Controls.Add(_auditListView);
        _auditPage.Controls.Add(auditCard);
    }

    private void RefreshAuditLogs()
    {
        if (_auditListView == null) return;
        _auditListView.Items.Clear();

        try
        {
            var dateStr = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var logs = _runtime.AuditLog.GetLogs(dateStr);
            foreach (var entry in logs)
            {
                var item = new ListViewItem(entry.Time)
                {
                    ForeColor = ThemeColors.TextPrimary,
                };
                item.SubItems.Add(entry.Device);
                item.SubItems.Add(entry.Action);
                _auditListView.Items.Add(item);
            }
        }
        catch
        {
            // audit log read fallback
        }
    }
    #endregion

    private void OnTimerTick()
    {
        if (_tabControl.SelectedIndex == 2)
        {
            RefreshPairingCode();
        }
        else if (_tabControl.SelectedIndex == 3)
        {
            UpdateMetrics();
        }
    }

    private async Task ToggleServerAsync()
    {
        if (_toggleServerButton == null) return;
        _toggleServerButton.Enabled = false;

        try
        {
            if (_runtime.IsServerRunning)
            {
                _toggleServerButton.Text = "Stopping…";
                await Task.Run(() => _runtime.StopServer());
            }
            else
            {
                _toggleServerButton.Text = "Starting…";
                await Task.Run(() => _runtime.StartServer());
            }
        }
        finally
        {
            _toggleServerButton.Enabled = true;
            UpdateUi();
        }
    }

    private void PostUpdateUi()
    {
        if (IsDisposed) return;
        try { BeginInvoke(UpdateUi); }
        catch { }
    }
}
