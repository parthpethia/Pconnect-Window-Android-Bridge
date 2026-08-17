using System.Buffers.Binary;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pconnect.Agent.Resilience;

namespace Pconnect.Agent.Services;

internal sealed class WebSocketHandler
{
    internal int WebRtcTimeoutMs { get; set; } = 5000;

    private readonly string _shutdownPassword;
    private readonly PairingService _pairing;
    private readonly PairedDevicesStore _paired;
    private readonly PcActions _pc;
    private readonly IUiActions _ui;
    private readonly FileTransferManager _fileTransfer = new();
    private readonly CustomCommandService _customCommands = new();
    private readonly AuditLogService _auditLog = new();
    private readonly SystemMetricsService _metrics = new();

    private readonly Action<string, string?>? _onDeviceAuthed;
    private readonly Action<string>? _onDeviceDisconnected;
    private SafeStartupOptions _safe = SafeStartupOptions.Normal;
    private readonly Func<(bool WsServing, bool DiscoveryUdp)>? _networkBindingState;

    // Capabilities list sent during handshake
    private static readonly string[] Capabilities =
    {
        "lock", "text", "launch", "show", "mouse", "keyboard", "volume",
        "brightness", "shutdown", "clipboard", "fileTransfer", "recentFiles",
        "keyCombo", "mediaKey", "screenCapture", "appList", "customCommands",
        "auditLog", "notification", "systemControl", "systemMetrics"
    };

    public AuditLogService AuditLog => _auditLog;

    internal void ConfigureSafeMode(SafeStartupOptions safe) => _safe = safe;

    private IReadOnlyList<string> AdvertisedCapabilities =>
        !_safe.IsSafeMode
            ? Capabilities
            : Capabilities.Where(static c => c is not ("screenCapture" or "customCommands")).ToArray();

    private IReadOnlyList<string> AdvertisedScreenStreamModes =>
        ScreenStreamNegotiation.AgentSupportedModes(_safe);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    public WebSocketHandler(
        PairingService pairing,
        PairedDevicesStore paired,
        PcActions pc,
        IUiActions ui,
        Action<string, string?>? onDeviceAuthed = null,
        Action<string>? onDeviceDisconnected = null,
        Func<(bool WsServing, bool DiscoveryUdp)>? networkBindingState = null)
    {
        _pairing = pairing;
        _paired = paired;
        _pc = pc;
        _ui = ui;
        _onDeviceAuthed = onDeviceAuthed;
        _onDeviceDisconnected = onDeviceDisconnected;
        _networkBindingState = networkBindingState;
        _shutdownPassword = Environment.GetEnvironmentVariable("PCONNECT_SHUTDOWN_PIN") ?? "1326";
    }

    public async Task HandleConnectionAsync(WebSocket ws, IPAddress? remoteIp, CancellationToken ct)
    {
        string? deviceId = null;
        string? deviceName = null;
        string deviceRole = "admin";
        var authed = false;
        ScreenCaptureService? screenCapture = null;
        ClipboardMonitor? clipboardMonitor = null;
        System.Threading.Timer? clipboardPollTimer = null;
        NotificationListenerService? notificationListener = null;
        System.Threading.Timer? autoLockTimer = null;
        var sessionNonceBytes = RandomNumberGenerator.GetBytes(16);
        long sessionEpoch = DateTime.UtcNow.Ticks;
        byte[]? integrityKey = null;
        var lastCmdSeq = 0;
        IReadOnlyList<string>? lastClientScreenStreamModes = null;
        bool isLanSession = LanAddressHelper.IsSameSubnet(remoteIp);
        int activeWebRtcTimeoutMs = isLanSession ? 2000 : WebRtcTimeoutMs;

        WebRtcSessionService? webRtcSession = null;
        ScreenCaptureDxgi? dxgiCapture = null;
        H264EncoderService? h264Encoder = null;
        Thread? webRtcCaptureThread = null;
        CancellationTokenSource? webRtcCts = null;
        string? negotiatedScreenStream = null;
        var inputDispatcher = new InputDispatcher(new KeyboardInjector());

        Action onInputBlocked = () =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        await SendAsync(ws, new { v = 1, type = "uipiBlocked" }, ct);
                    }
                }
                catch { /* ignore */ }
            });
        };
        KeyboardInjector.InputBlocked += onInputBlocked;

        void CleanupWebRtcResources()
        {
            webRtcCts?.Cancel();
            bool joined = true;
            if (webRtcCaptureThread != null && webRtcCaptureThread.IsAlive)
            {
                joined = webRtcCaptureThread.Join(4000);
                if (!joined)
                {
                    Console.WriteLine("[WebSocketHandler] Warning: WebRTC capture thread did not terminate within 4000ms. Handing off to background thread for deferred disposal.");
                    
                    var captureToDispose = dxgiCapture;
                    var encoderToDispose = h264Encoder;
                    var threadToJoin = webRtcCaptureThread;

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            threadToJoin.Join();
                            captureToDispose?.Dispose();
                            encoderToDispose?.Dispose();
                            Console.WriteLine("[WebSocketHandler] Background WebRTC cleanup complete.");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[WebSocketHandler] Error in background WebRTC cleanup: {ex.Message}");
                        }
                    });

                    dxgiCapture = null;
                    h264Encoder = null;
                }
            }
            webRtcCaptureThread = null;

            webRtcSession?.Dispose();
            webRtcSession = null;

            if (joined)
            {
                dxgiCapture?.Dispose();
                dxgiCapture = null;
                h264Encoder?.Dispose();
                h264Encoder = null;
            }
        }

        async Task HandleWebRtcFallbackAsync()
        {
            CleanupWebRtcResources();

            // Prefer jpeg-bin-v1 if client supports it, otherwise fall back to jpeg-v1
            string fallbackMode;
            if (lastClientScreenStreamModes != null &&
                lastClientScreenStreamModes.Contains(ScreenStreamNegotiation.JpegBinV1))
            {
                fallbackMode = ScreenStreamNegotiation.JpegBinV1;
            }
            else
            {
                fallbackMode = ScreenStreamNegotiation.JpegV1;
            }

            negotiatedScreenStream = fallbackMode;
            await SendAsync(ws, new { v = 1, type = "webrtcFallback", mode = fallbackMode }, ct);

            // Transparently start the capture loop in the negotiated fallback mode
            if (!_safe.DisableScreenCapture)
            {
                screenCapture?.Dispose();
                if (fallbackMode == ScreenStreamNegotiation.JpegBinV1)
                {
                    screenCapture = new ScreenCaptureService((byte[] raw, int w, int h) =>
                    {
                        _ = SendBinaryFrameAsync(ws, raw, w, h, ct);
                    });
                }
                else
                {
                    screenCapture = new ScreenCaptureService((b64, w, h) =>
                    {
                        _ = SendScreenFrameAsync(ws, b64, w, h, ct);
                    });
                }
                screenCapture.Start(120, 720, 60);
                _auditLog.Log(deviceName, $"webrtcFallback:screenCaptureStart:{fallbackMode}");
            }
        }

        bool PassesClientPolicy(Dictionary<string, JsonElement> m, out string? err)
        {
            err = null;
            var ver = m.GetStringOrNull("clientVersion") ?? "0.0.0";
            if (!SemverUtility.IsAtLeast(ver, OperationalConfigRuntime.MinMobileSemver))
            {
                err = $"Mobile app update required (minimum {OperationalConfigRuntime.MinMobileSemver}).";
                return false;
            }

            var proto = m.GetIntOrDefault("proto", 1);
            if (proto < OperationalConfigRuntime.MinClientProto)
            {
                err = $"Mobile app update required (protocol version {OperationalConfigRuntime.MinClientProto} or newer).";
                return false;
            }

            return true;
        }

        // Helper to start notification mirroring after auth
        async Task StartNotificationListenerAsync()
        {
            if (_safe.DisableNotificationMirror)
            {
                return;
            }

            try
            {
                var listener = new NotificationListenerService(
                    async json =>
                    {
                        if (ws.State == WebSocketState.Open)
                        {
                            var bytes = Encoding.UTF8.GetBytes(json);
                            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                        }
                    },
                    _auditLog);

                if (await listener.RequestAccessAsync())
                {
                    listener.Start();
                    notificationListener = listener;
                }
                else
                {
                    Console.WriteLine("[WebSocketHandler] Notification listener access denied by user.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebSocketHandler] Failed to start notification listener: {ex.Message}");
            }
        }

        await SendAsync(ws, new
        {
            v = 1,
            type = "welcome",
            pcName = Environment.MachineName,
            sessionNonce = Convert.ToHexString(sessionNonceBytes),
            sessionEpoch = sessionEpoch,
            wssPort = AgentRuntime.DefaultWssPort,
        }, ct);

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var msg = await ReceiveJsonAsync(ws, ct);
                if (msg is null) break;

                if (!msg.TryGetValue("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                {
                    await SendAsync(ws, new { v = 1, type = "error", message = "Missing type" }, ct);
                    continue;
                }

                var type = typeEl.GetString();
                if (string.IsNullOrEmpty(type)) continue;

                var typeRaw = type;
                type = type.Trim();
                var typeKey = type.ToLowerInvariant();

                if (!authed)
                {
                    if (typeKey == "hello")
                    {
                        var proto = msg.GetIntOrDefault("proto", 1);
                        if (proto > 2)
                        {
                            var envelope = StructuredErrorEnvelope.Mismatch("Incompatible major protocol version");
                            await SendAsync(ws, envelope, ct);
                            continue;
                        }

                        if (!PassesClientPolicy(msg, out var policyErr))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = policyErr }, ct);
                            continue;
                        }

                        lastClientScreenStreamModes = msg.GetStringArrayOrNull("screenStreamModes");

                        deviceId = msg.GetStringOrNull("deviceId");
                        var token = msg.GetStringOrNull("token");
                        deviceName = msg.GetStringOrNull("deviceName");

                        if (string.IsNullOrWhiteSpace(deviceName) && deviceId is not null)
                            deviceName = _paired.GetDeviceName(deviceId);

                        if (deviceId is not null && token is not null && _paired.IsPaired(deviceId, token))
                        {
                            authed = true;
                            deviceRole = _paired.GetRole(deviceId);
                            proto = msg.GetIntOrDefault("proto", 1);
                            if (proto >= 2)
                            {
                                integrityKey = CommandIntegrity.TryDeriveIntegrityKey(token, sessionNonceBytes);
                            }

                            _onDeviceAuthed?.Invoke(deviceId, deviceName);
                            _auditLog.Log(deviceName, "connected");
                            var clientStreamModes = lastClientScreenStreamModes;
                            var serverStreamModes = AdvertisedScreenStreamModes;
                            var negotiatedStream = ScreenStreamNegotiation.Negotiate(clientStreamModes, serverStreamModes);
                            await SendAsync(ws, new
                            {
                                v = 1, type = "helloAck",
                                pcName = Environment.MachineName,
                                role = deviceRole,
                                capabilities = AdvertisedCapabilities,
                                screenStreamModes = serverStreamModes,
                                screenStream = negotiatedStream,
                            }, ct);
                            negotiatedScreenStream = negotiatedStream;
                            await StartNotificationListenerAsync();

                            // Start clipboard monitoring (PC → phone sync)
                            clipboardMonitor = new ClipboardMonitor(text =>
                            {
                                if (ws.State != WebSocketState.Open || string.IsNullOrEmpty(text)) return;
                                var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                                var json = System.Text.Json.JsonSerializer.Serialize(new
                                {
                                    v = 1, type = "clipboardUpdate", data = b64, format = "text/plain", source = "system"
                                });
                                var bytes = Encoding.UTF8.GetBytes(json);
                                // Fire-and-forget: avoid blocking the timer thread with .GetAwaiter().GetResult()
                                _ = Task.Run(async () =>
                                {
                                    try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
                                    catch { /* connection may have closed */ }
                                });
                            });
                            clipboardPollTimer = new System.Threading.Timer(_ => clipboardMonitor?.Poll(), null, 500, 500);
                        }
                        else
                        {
                            await SendAsync(ws, new { v = 1, type = "authRequired", pairing = new { method = "code" } }, ct);
                        }
                        continue;
                    }

                    if (typeKey == "pair")
                    {
                        if (!PassesClientPolicy(msg, out var policyErrPair))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = policyErrPair }, ct);
                            continue;
                        }

                        lastClientScreenStreamModes = msg.GetStringArrayOrNull("screenStreamModes") ?? lastClientScreenStreamModes;

                        deviceId = msg.GetStringOrNull("deviceId") ?? deviceId;
                        var code = msg.GetStringOrNull("code");
                        deviceName = msg.GetStringOrNull("deviceName");

                        if (deviceId is null)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Missing deviceId" }, ct);
                            continue;
                        }

                        if (!_pairing.ValidateCode(code))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid pairing code" }, ct);
                            continue;
                        }

                        var token = _paired.PairNewDevice(deviceId, deviceName);
                        authed = true;
                        deviceRole = _paired.GetRole(deviceId);
                        var proto = msg.GetIntOrDefault("proto", 1);
                        if (proto >= 2)
                        {
                            integrityKey = CommandIntegrity.TryDeriveIntegrityKey(token, sessionNonceBytes);
                        }

                        _onDeviceAuthed?.Invoke(deviceId, deviceName);
                        _auditLog.Log(deviceName, "paired");

                        await SendAsync(ws, new { v = 1, type = "paired", deviceId, token, role = deviceRole }, ct);
                        var clientStreamModesPair = lastClientScreenStreamModes;
                        var serverStreamModesPair = AdvertisedScreenStreamModes;
                        var negotiatedStreamPair = ScreenStreamNegotiation.Negotiate(clientStreamModesPair, serverStreamModesPair);
                        await SendAsync(ws, new
                        {
                            v = 1, type = "helloAck",
                            pcName = Environment.MachineName,
                            role = deviceRole,
                            capabilities = AdvertisedCapabilities,
                            screenStreamModes = serverStreamModesPair,
                            screenStream = negotiatedStreamPair,
                        }, ct);
                        negotiatedScreenStream = negotiatedStreamPair;
                        await StartNotificationListenerAsync();

                        // Start clipboard monitoring (PC → phone sync)
                        clipboardMonitor = new ClipboardMonitor(text =>
                        {
                            if (ws.State != WebSocketState.Open || string.IsNullOrEmpty(text)) return;
                            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                            var json = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                v = 1, type = "clipboardUpdate", data = b64, format = "text/plain", source = "system"
                            });
                            var bytes = Encoding.UTF8.GetBytes(json);
                            // Fire-and-forget: avoid blocking the timer thread with .GetAwaiter().GetResult()
                            _ = Task.Run(async () =>
                            {
                                try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
                                catch { /* connection may have closed */ }
                            });
                        });
                        clipboardPollTimer = new System.Threading.Timer(_ => clipboardMonitor?.Poll(), null, 500, 500);
                        continue;
                    }

                    await SendAsync(ws, new { v = 1, type = "authRequired", pairing = new { method = "code" } }, ct);
                    continue;
                }

                // ── Role checking helpers ──
                bool RequireAdmin()
                {
                    return deviceRole == "admin";
                }
                bool RequireMediaOrAdmin()
                {
                    return deviceRole is "admin" or "media_only";
                }

                if (OperationalConfigRuntime.EmergencyDisableRemote)
                {
                    await SendAsync(ws, new { v = 1, type = "error", message = "Agent paused by operator policy" }, ct);
                    continue;
                }

                bool TryConsumeMac(string canon, out string? err)
                {
                    err = null;
                    var seq = msg.GetIntOrDefault("cmdSeq", 0);
                    var mac = msg.GetStringOrNull("cmdMac");
                    var require = OperationalConfigRuntime.RequireSensitiveMac || integrityKey is not null;
                    if (!require)
                    {
                        return true;
                    }

                    if (integrityKey is null)
                    {
                        err = "Upgrade mobile app for verified commands";
                        return false;
                    }

                    if (seq <= lastCmdSeq)
                    {
                        err = "Stale cmdSeq";
                        return false;
                    }

                    if (!CommandIntegrity.TryVerifyMac(integrityKey, sessionEpoch, seq, canon, mac))
                    {
                        _auditLog.Log(deviceName, $"security:macRejected:epoch={sessionEpoch},seq={seq}");
                        err = "Invalid cmdMac or session replay detected";
                        return false;
                    }

                    lastCmdSeq = seq;
                    return true;
                }

                // ── Authenticated command dispatch ──
                switch (typeKey)
                {
                    case "lock":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        _auditLog.Log(deviceName, "lock");
                        await SendAsync(ws, _pc.Lock()
                            ? new { v = 1, type = "ok" }
                            : (object)new { v = 1, type = "error", message = "Lock failed" }, ct);
                        break;

                    case "input":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var backspaces = msg.GetIntOrDefault("backspaces", 0);
                        var text = msg.GetStringOrNull("text") ?? string.Empty;
                        _pc.TypeText(backspaces, text);
                        _auditLog.Log(deviceName, "input");
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "replacealltext":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var replaceText = msg.GetStringOrNull("text") ?? string.Empty;
                        _pc.ReplaceAllText(replaceText);
                        _auditLog.Log(deviceName, "replaceAllText");
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "launch":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var command = msg.GetStringOrNull("command");
                        var args = msg.GetStringArrayOrNull("args");
                        if (string.IsNullOrWhiteSpace(command))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Missing command" }, ct);
                            break;
                        }

                        var argCanon = args is null ? "" : string.Join('\x1e', args);
                        if (!TryConsumeMac($"launch|{command}|{argCanon}", out var macErrL))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = macErrL ?? "Command verification failed" }, ct);
                            break;
                        }

                        _pc.Launch(command!, args);
                        _auditLog.Log(deviceName, $"launch:{command}");
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "launchapp":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var exePath = msg.GetStringOrNull("exePath");
                        if (string.IsNullOrWhiteSpace(exePath))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Missing exePath" }, ct);
                            break;
                        }

                        if (!TryConsumeMac($"launchapp|{exePath}", out var macErrLa))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = macErrLa ?? "Command verification failed" }, ct);
                            break;
                        }

                        _pc.Launch(exePath!, null);
                        _auditLog.Log(deviceName, $"launchApp:{exePath}");
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "show":
                        _ui.ShowAgentUi();
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "mousemove":
                        if (!RequireAdmin()) break; // silent for perf
                        _pc.MouseMove(msg.GetIntOrDefault("dx", 0), msg.GetIntOrDefault("dy", 0));
                        break;

                    case "mouseset":
                    case "mouseto":
                        if (!RequireAdmin()) break;
                        var rx = msg.GetDoubleOrDefault("xRatio", -1.0);
                        var ry = msg.GetDoubleOrDefault("yRatio", -1.0);
                        if (rx >= 0 && ry >= 0)
                        {
                            _pc.MoveMouseNormalized(rx, ry);
                        }
                        else
                        {
                            _pc.MoveMouseTo(msg.GetIntOrDefault("x", 0), msg.GetIntOrDefault("y", 0));
                        }
                        break;

                    case "mousescroll":
                        if (!RequireAdmin()) break;
                        _pc.MouseScroll(msg.GetIntOrDefault("dy", 0));
                        break;

                    case "mousebutton":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var btn = msg.GetStringOrNull("button") ?? "";
                        var act = msg.GetStringOrNull("action") ?? "";
                        if (string.IsNullOrWhiteSpace(btn) || string.IsNullOrWhiteSpace(act))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Missing button/action" }, ct);
                            break;
                        }
                        var mbRx = msg.GetDoubleOrDefault("xRatio", -1.0);
                        var mbRy = msg.GetDoubleOrDefault("yRatio", -1.0);
                        if (mbRx >= 0 && mbRy >= 0 && act.Trim().ToLowerInvariant() == "click")
                        {
                            _pc.MoveAndClickNormalized(mbRx, mbRy, btn);
                        }
                        else
                        {
                            _pc.MouseButton(btn, act);
                        }
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "key":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var vk = msg.GetIntOrDefault("vk", 0);
                        var keyAction = msg.GetStringOrNull("action") ?? "";
                        var extended = msg.GetBoolOrDefault("extended", false);
                        if (vk <= 0 || vk > 0xFF)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid vk" }, ct);
                            break;
                        }
                        if (string.IsNullOrWhiteSpace(keyAction))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Missing action" }, ct);
                            break;
                        }
                        _pc.Key((ushort)vk, keyAction, extended);
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "keycombo":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var keys = msg.GetStringArrayOrNull("keys");
                        if (keys == null || keys.Count == 0)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Missing keys" }, ct);
                            break;
                        }
                        var comboOk = KeyComboService.Execute(keys);
                        _auditLog.Log(deviceName, $"keyCombo:{string.Join("+", keys)}");
                        await SendAsync(ws, comboOk
                            ? new { v = 1, type = "ok" }
                            : (object)new { v = 1, type = "error", message = "Key combo failed" }, ct);
                        break;

                    case "mediakey":
                        if (!RequireMediaOrAdmin()) { await SendRoleError(ws, ct); break; }
                        var mediaKey = msg.GetStringOrNull("key") ?? "";
                        var mediaOk = MediaKeyService.Send(mediaKey);
                        _auditLog.Log(deviceName, $"mediaKey:{mediaKey}");
                        await SendAsync(ws, mediaOk
                            ? new { v = 1, type = "ok" }
                            : (object)new { v = 1, type = "error", message = "Unknown media key" }, ct);
                        break;

                    case "setvolume":
                        if (!RequireMediaOrAdmin()) { await SendRoleError(ws, ct); break; }
                        var volLevel = msg.GetIntOrDefault("level", -1);
                        if (volLevel < 0 || volLevel > 100)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid level" }, ct);
                            break;
                        }
                        await SendAsync(ws, _pc.SetVolume(volLevel)
                            ? new { v = 1, type = "ok" }
                            : (object)new { v = 1, type = "error", message = "Volume set failed" }, ct);
                        break;

                    case "setbrightness":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var brLevel = msg.GetIntOrDefault("level", -1);
                        if (brLevel < 0 || brLevel > 100)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid level" }, ct);
                            break;
                        }
                        await SendAsync(ws, _pc.SetBrightness(brLevel)
                            ? new { v = 1, type = "ok" }
                            : (object)new { v = 1, type = "error", message = "Brightness set failed" }, ct);
                        break;

                    case "shutdown":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var password = msg.GetStringOrNull("password") ?? msg.GetStringOrNull("pin");
                        if (string.IsNullOrWhiteSpace(password))
                        {
                            await SendAsync(ws, StructuredErrorEnvelope.Unauthorized("Shutdown PIN required"), ct);
                            break;
                        }

                        bool isRateLimited = false;
                        string? pinErr = null;
                        if (deviceId == null || !_paired.VerifyShutdownPin(deviceId, password.Trim(), out isRateLimited, out pinErr))
                        {
                            _auditLog.Log(deviceName, $"security:shutdownPinRefused:deviceId={deviceId}");
                            var errEnv = isRateLimited
                                ? StructuredErrorEnvelope.RateLimited(pinErr ?? "Rate limited")
                                : StructuredErrorEnvelope.Unauthorized(pinErr ?? "Invalid shutdown PIN");
                            await SendAsync(ws, errEnv, ct);
                            break;
                        }

                        if (!TryConsumeMac($"shutdown|{password.Trim()}", out var macErrS))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = macErrS ?? "Command verification failed" }, ct);
                            break;
                        }

                        _auditLog.Log(deviceName, "shutdown");
                        await SendAsync(ws, _pc.Shutdown()
                            ? new { v = 1, type = "ok" }
                            : (object)new { v = 1, type = "error", message = "Shutdown failed" }, ct);
                        break;

                    case "clipboardset":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var clipData = msg.GetStringOrNull("data");
                        if (string.IsNullOrWhiteSpace(clipData))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Missing clipboard data" }, ct);
                            break;
                        }
                        try
                        {
                            var bytes = Convert.FromBase64String(clipData);
                            var clipText = Encoding.UTF8.GetString(bytes);
                            _pc.SetClipboard(clipText);
                            _auditLog.Log(deviceName, "clipboardSet");
                            await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        }
                        catch (Exception ex)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = $"Clipboard set failed: {ex.Message}" }, ct);
                        }
                        break;

                    case "systemcontrol":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var sysAction = (msg.GetStringOrNull("action") ?? "").Trim().ToLowerInvariant();
                        _auditLog.Log(deviceName, $"systemControl:{sysAction}");
                        switch (sysAction)
                        {
                            case "sleep":
                                await SendAsync(ws, _pc.Sleep() ? new { v = 1, type = "ok" } : (object)new { v = 1, type = "error", message = "Sleep failed" }, ct);
                                break;
                            case "restart":
                                await SendAsync(ws, _pc.Restart() ? new { v = 1, type = "ok" } : (object)new { v = 1, type = "error", message = "Restart failed" }, ct);
                                break;
                            case "taskmanager":
                                _pc.OpenTaskManager();
                                await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                                break;
                            case "desktop":
                                _pc.ShowDesktop();
                                await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                                break;
                            case "taskview":
                                _pc.TaskView();
                                await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                                break;
                            case "mute":
                                _pc.ToggleMuteAudio();
                                await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                                break;
                            default:
                                await SendAsync(ws, new { v = 1, type = "error", message = $"Unknown systemControl action: {sysAction}" }, ct);
                                break;
                        }
                        break;

                    case "getsystemmetrics":
                        var snapshot = _metrics.GetSnapshot();
                        await SendAsync(ws, new
                        {
                            v = 1,
                            type = "systemMetricsResponse",
                            cpuPercent = snapshot.CpuPercent,
                            ramPercent = snapshot.RamPercent,
                            totalRamMb = snapshot.TotalRamMb,
                            usedRamMb = snapshot.UsedRamMb,
                            uptimeSeconds = (long)snapshot.Uptime.TotalSeconds,
                            processCount = snapshot.ProcessCount
                        }, ct);
                        break;

                    case "filetransferstart":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var ftId = msg.GetStringOrNull("id");
                        var ftFile = msg.GetStringOrNull("filename");
                        var ftSize = msg.GetLongOrDefault("size", 0L);
                        var ftSha = msg.GetStringOrNull("sha256");
                        var ftProto = msg.GetIntOrDefault("protocolVersion", 1);
                        if (string.IsNullOrWhiteSpace(ftId) || string.IsNullOrWhiteSpace(ftFile) || ftSize <= 0)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid transfer parameters" }, ct);
                            break;
                        }
                        if (ftProto != 2)
                        {
                            await SendAsync(ws, new { v = 1, type = "fileTransferAck", id = ftId, ready = false, error = $"Protocol version mismatch: expected v2, got v{ftProto}", protocolVersion = 2 }, ct);
                            break;
                        }
                        var ftResult = _fileTransfer.StartTransfer(ftId, ftFile, ftSize, ftSha, out var ftError);
                        _auditLog.Log(deviceName, $"fileTransferStart:{ftFile}");
                        await SendAsync(ws, ftResult != null
                            ? (object)new { v = 1, type = "fileTransferAck", id = ftId, ready = true, protocolVersion = 2 }
                            : new { v = 1, type = "fileTransferAck", id = ftId, ready = false, error = ftError ?? "Failed to start transfer", protocolVersion = 2 }, ct);
                        break;

                    case "filetransferresume":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var rId = msg.GetStringOrNull("id");
                        var rFile = msg.GetStringOrNull("filename");
                        var rSize = msg.GetLongOrDefault("size", 0L);
                        var rSha = msg.GetStringOrNull("sha256");
                        var rProto = msg.GetIntOrDefault("protocolVersion", 1);
                        if (string.IsNullOrWhiteSpace(rId) || string.IsNullOrWhiteSpace(rFile) || rSize <= 0)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid resume parameters" }, ct);
                            break;
                        }
                        if (rProto != 2)
                        {
                            await SendAsync(ws, new { v = 1, type = "fileTransferResumeAck", id = rId, ready = false, error = $"Protocol version mismatch: expected v2, got v{rProto}", protocolVersion = 2 }, ct);
                            break;
                        }
                        var resOk = _fileTransfer.ResumeTransfer(rId, rFile, rSize, rSha, out var highestContiguous, out var receivedSet, out var rError);
                        _auditLog.Log(deviceName, $"fileTransferResume:{rFile}");
                        await SendAsync(ws, resOk
                            ? (object)new { v = 1, type = "fileTransferResumeAck", id = rId, ready = true, highestContiguousChunk = highestContiguous, receivedChunks = receivedSet.ToList(), protocolVersion = 2 }
                            : new { v = 1, type = "fileTransferResumeAck", id = rId, ready = false, error = rError ?? "Failed to resume transfer", protocolVersion = 2 }, ct);
                        break;

                    case "filetransferdiscard":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var dId = msg.GetStringOrNull("id");
                        if (!string.IsNullOrWhiteSpace(dId))
                        {
                            _fileTransfer.DiscardTransfer(dId!);
                            _auditLog.Log(deviceName, $"fileTransferDiscard:{dId}");
                        }
                        await SendAsync(ws, new { v = 1, type = "fileTransferDiscardAck", id = dId, success = true }, ct);
                        break;

                    case "filetransferlistdir":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var dirPath = msg.GetStringOrNull("path");
                        var items = _fileTransfer.ListAllowedDirectory(dirPath);
                        await SendAsync(ws, new { v = 1, type = "fileTransferDirList", path = dirPath, items, status = "ok" }, ct);
                        break;

                    case "filetransferdownloadstart":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var dlId = msg.GetStringOrNull("id");
                        var dlPath = msg.GetStringOrNull("path");
                        if (string.IsNullOrWhiteSpace(dlId) || string.IsNullOrWhiteSpace(dlPath))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid download parameters" }, ct);
                            break;
                        }
                        var dlOk = _fileTransfer.StartDownload(dlId, dlPath!, out var dlName, out var dlSize, out var dlSha, out var dlError);
                        _auditLog.Log(deviceName, $"fileTransferDownloadStart:{dlName}");
                        await SendAsync(ws, dlOk
                            ? (object)new { v = 1, type = "fileTransferDownloadAck", id = dlId, ready = true, filename = dlName, size = dlSize, sha256 = dlSha, protocolVersion = 2 }
                            : new { v = 1, type = "fileTransferDownloadAck", id = dlId, ready = false, error = dlError ?? "Failed to start download", protocolVersion = 2 }, ct);
                        break;

                    case "filetransferdownloadchunk":
                        if (!RequireAdmin()) break;
                        var dlcId = msg.GetStringOrNull("id");
                        var dlcPath = msg.GetStringOrNull("path");
                        var dlcIdx = msg.GetIntOrDefault("chunkIndex", -1);
                        if (string.IsNullOrWhiteSpace(dlcId) || string.IsNullOrWhiteSpace(dlcPath) || dlcIdx < 0)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid download chunk parameters" }, ct);
                            break;
                        }
                        var chunkBytes = _fileTransfer.ReadDownloadChunk(dlcPath!, dlcIdx);
                        if (chunkBytes != null)
                        {
                            var b64Data = Convert.ToBase64String(chunkBytes);
                            await SendAsync(ws, new { v = 1, type = "fileTransferDownloadChunkData", id = dlcId, chunkIndex = dlcIdx, data = b64Data, size = chunkBytes.Length }, ct);
                        }
                        else
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Failed to read download chunk" }, ct);
                        }
                        break;

                    case "filetransferchunk":
                        if (!RequireAdmin()) break;
                        var chId = msg.GetStringOrNull("id");
                        var chIdx = msg.GetIntOrDefault("chunkIndex", -1);
                        var chData = msg.GetStringOrNull("data");
                        if (string.IsNullOrWhiteSpace(chId) || chIdx < 0 || string.IsNullOrWhiteSpace(chData))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid chunk parameters" }, ct);
                            break;
                        }
                        try
                        {
                            var chBytes = Convert.FromBase64String(chData);
                            if (_fileTransfer.WriteChunk(chId, chIdx, chBytes))
                            {
                                // 1. Flow control: immediate ACK for in-flight windowing
                                await SendAsync(ws, new
                                {
                                    v = 1, type = "fileTransferAckChunk", id = chId, chunkIndex = chIdx
                                }, ct);

                                // 2. UI progress update: throttled to 250ms interval
                                if (_fileTransfer.ShouldReportProgress(chId, 250))
                                {
                                    var prog = _fileTransfer.GetProgress(chId);
                                    await SendAsync(ws, new
                                    {
                                        v = 1, type = "fileTransferProgress", id = chId,
                                        chunkIndex = chIdx, received = prog?.received ?? 0, total = prog?.total ?? 0
                                    }, ct);
                                }
                            }
                            else
                            {
                                await SendAsync(ws, new { v = 1, type = "error", message = "Failed to write chunk" }, ct);
                            }
                        }
                        catch (Exception ex)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = $"Chunk write error: {ex.Message}" }, ct);
                        }
                        break;

                    case "filetransfercomplete":
                        if (!RequireAdmin()) break;
                        var fcId = msg.GetStringOrNull("id");
                        if (string.IsNullOrWhiteSpace(fcId))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Missing transfer id" }, ct);
                            break;
                        }
                        await SendAsync(ws, _fileTransfer.CompleteTransfer(fcId)
                            ? (object)new { v = 1, type = "fileTransferComplete", id = fcId, status = "success" }
                            : new { v = 1, type = "error", message = "Failed to complete transfer" }, ct);
                        break;

                    case "filetransferabort":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var faId = msg.GetStringOrNull("id");
                        if (!string.IsNullOrWhiteSpace(faId)) _fileTransfer.AbortTransfer(faId);
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "listrecentfiles":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var limit = msg.GetIntOrDefault("limit", 20);
                        var recentFiles = RecentFilesHelper.GetRecentFiles(limit);
                        await SendAsync(ws, new
                        {
                            v = 1, type = "recentFilesList",
                            files = recentFiles.Select(f => new { path = f.Path, name = f.Name, modified = f.Modified, size = f.Size }).ToList(),
                            status = "ok"
                        }, ct);
                        break;

                    case "webrtcoffer":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        if (negotiatedScreenStream != ScreenStreamNegotiation.WebRtcV1) break;
                        var offerSdp = msg.GetStringOrNull("sdp");
                        if (offerSdp == null) break;

                        var clientWidth = msg.GetIntOrDefault("width", 720);
                        var clientQuality = msg.GetIntOrDefault("quality", 65);

                        try
                        {
                            CleanupWebRtcResources();

                            webRtcCts = new CancellationTokenSource();
                            var localCts = webRtcCts;

                            dxgiCapture = new ScreenCaptureDxgi();
                            
                            int targetWidth = (int)dxgiCapture.Width;
                            int targetHeight = (int)dxgiCapture.Height;
                            if (clientWidth > 0 && clientWidth < dxgiCapture.Width)
                            {
                                double ratio = (double)clientWidth / dxgiCapture.Width;
                                targetWidth = (clientWidth / 2) * 2;
                                targetHeight = ((int)(dxgiCapture.Height * ratio) / 2) * 2;
                            }

                            int targetBitrate = ScreenStreamNegotiation.GetWebRtcTargetBitrate(clientQuality);

                            h264Encoder = new H264EncoderService();
                            h264Encoder.Initialize(targetWidth, targetHeight, 30, targetBitrate, dxgiCapture.Device);
                            Console.WriteLine($"[WebSocketHandler] Selected H.264 Encoder Tier: {h264Encoder.EncoderName} (Hardware: {h264Encoder.IsHardware}, GPU-resident: {h264Encoder.UseGpuPath})");
                            _auditLog.Log(deviceName, $"encoderTier:{h264Encoder.EncoderName}");

                            bool isConnected = false;
                            bool isFailed = false;

                            bool hostOnlyAttempt = isLanSession;
                            webRtcSession = new WebRtcSessionService(
                                onInputPacket: (data) =>
                                {
                                    inputDispatcher.Dispatch(data);
                                },
                                onIceCandidate: async (candidate) =>
                                {
                                    await SendAsync(ws, new
                                    {
                                        v = 1,
                                        type = "webrtcIce",
                                        candidate = candidate,
                                        sdpMid = "0",
                                        sdpMLineIndex = 0
                                    }, ct);
                                },
                                onConnected: () =>
                                {
                                    lock (localCts)
                                    {
                                        if (isConnected || isFailed) return;
                                        isConnected = true;
                                    }
                                    Console.WriteLine("[WebSocketHandler] WebRTC connected. Starting capture loop.");
                                    var aimdController = new AimdBitrateController(targetBitrate, targetBitrate, ScreenStreamNegotiation.MinBitrateFloorKbps);
                                    long lastAimdTick = 0;

                                    webRtcCaptureThread = new Thread(() =>
                                    {
                                        try
                                        {
                                             SendAsync(ws, new { v = 1, type = "webrtcReady" }, ct).GetAwaiter().GetResult();

                                             byte[]? processedBuffer = null;

                                             while (!localCts.IsCancellationRequested && ws.State == WebSocketState.Open)
                                             {
                                                 long nowTicks = Environment.TickCount64;
                                                 if (nowTicks - lastAimdTick >= 300)
                                                 {
                                                     lastAimdTick = nowTicks;
                                                     double lossFraction = webRtcSession?.GetLastLossFraction() ?? 0.0;
                                                     double rttMs = webRtcSession?.GetLastRttMs() ?? 10.0;

                                                     int nextRate = aimdController.Step(lossFraction, rttMs);
                                                     if (nextRate < 0)
                                                     {
                                                         Console.WriteLine("[WebSocketHandler] Target bitrate dropped below 800 Kbps floor. Triggering JPEG fallback.");
                                                         _ = HandleWebRtcFallbackAsync();
                                                         break;
                                                     }
                                                     else if (h264Encoder != null && nextRate != h264Encoder.CurrentBitrateKbps)
                                                     {
                                                         h264Encoder.UpdateBitrate(nextRate);
                                                     }
                                                 }

                                                 DxgiFrame? frame = null;
                                                 try
                                                 {
                                                     frame = dxgiCapture.AcquireNextFrame(33);
                                                 }
                                                 catch (Exception ex)
                                                 {
                                                     Console.WriteLine($"[WebSocketHandler] DXGI capture error: {ex.Message}. Retrying after 500ms backoff and re-initialization.");
                                                     Thread.Sleep(500);
                                                      try
                                                      {
                                                          dxgiCapture.Initialize();
                                                          h264Encoder?.RequestKeyframe();
                                                      }
                                                     catch (Exception initEx)
                                                     {
                                                         Console.WriteLine($"[WebSocketHandler] DXGI re-initialization failed: {initEx.Message}");
                                                     }
                                                     continue;
                                                 }

                                                 if (frame == null)
                                                 {
                                                     Thread.Sleep(10);
                                                     continue;
                                                 }

                                                 try
                                                 {
                                                     var session = webRtcSession;
                                                     if (h264Encoder != null && session != null)
                                                     {
                                                         ReadOnlyMemory<byte> nals;
                                                         try
                                                         {
                                                             if (h264Encoder.UseGpuPath)
                                                             {
                                                                 nals = h264Encoder.EncodeGpuTexture(frame.Texture, targetWidth, targetHeight, false);
                                                             }
                                                             else
                                                             {
                                                                 byte[]? rawBytes = dxgiCapture.CopyFrameToCpu(frame.Texture);
                                                                 if (rawBytes != null)
                                                                 {
                                                                     if (processedBuffer == null || processedBuffer.Length != targetWidth * targetHeight * 4)
                                                                     {
                                                                         processedBuffer = new byte[targetWidth * targetHeight * 4];
                                                                     }
                                                                     ProcessCpuFrame(rawBytes, (int)dxgiCapture.Width, (int)dxgiCapture.Height, processedBuffer, targetWidth, targetHeight);
                                                                     nals = h264Encoder.Encode(processedBuffer, targetWidth, targetHeight, false);
                                                                 }
                                                                 else
                                                                 {
                                                                     nals = ReadOnlyMemory<byte>.Empty;
                                                                 }
                                                             }
                                                         }
                                                         catch (Exception encEx)
                                                         {
                                                             Console.WriteLine($"[WebSocketHandler] Encoder error (likely resolution/mode change): {encEx.Message}. Re-initializing encoder.");
                                                             try
                                                             {
                                                                 dxgiCapture.Initialize();
                                                                 int newWidth = (int)dxgiCapture.Width;
                                                                 int newHeight = (int)dxgiCapture.Height;
                                                                 if (clientWidth > 0 && clientWidth < newWidth)
                                                                 {
                                                                     double ratio = (double)clientWidth / newWidth;
                                                                     targetWidth = (clientWidth / 2) * 2;
                                                                     targetHeight = ((int)(newHeight * ratio) / 2) * 2;
                                                                 }
                                                                 else
                                                                 {
                                                                     targetWidth = (newWidth / 2) * 2;
                                                                     targetHeight = (newHeight / 2) * 2;
                                                                 }
                                                                 h264Encoder?.Dispose();
                                                                 h264Encoder = new H264EncoderService();
                                                                 h264Encoder.Initialize(targetWidth, targetHeight, 30, targetBitrate, dxgiCapture.Device);
                                                             }
                                                             catch (Exception reinitEx)
                                                             {
                                                                 Console.WriteLine($"[WebSocketHandler] Failed to re-initialize encoder during recovery: {reinitEx.Message}");
                                                             }
                                                             nals = ReadOnlyMemory<byte>.Empty;
                                                         }
                                  
                                                         if (nals.Length > 0)
                                                         {
                                                             session.SendVideoFrame(nals, 33);
                                                         }
                                                     }
                                                 }
                                                 finally
                                                 {
                                                     frame.Release();
                                                 }
 
                                                 Thread.Sleep(20);
                                             }
                                         }
                                         catch (Exception ex)
                                         {
                                             Console.WriteLine($"[WebSocketHandler] WebRTC capture thread error: {ex.Message}");
                                         }
                                     })
                                     {
                                         IsBackground = true,
                                         Name = "PconnectWebRtcCapture"
                                     };
                                     webRtcCaptureThread.Start();
                                },
                                onFailed: async (reason) =>
                                {
                                    lock (localCts)
                                    {
                                        if (isFailed) return;
                                        isFailed = true;
                                    }
                                    Console.WriteLine($"[WebSocketHandler] WebRTC connection failed: {reason}");
                                    await HandleWebRtcFallbackAsync();
                                },
                                hostOnly: hostOnlyAttempt
                            );

                            string answerSdp = await webRtcSession.ProcessOfferAndCreateAnswer(offerSdp);
                            await SendAsync(ws, new { v = 1, type = "webrtcAnswer", sdp = answerSdp }, ct);

                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(activeWebRtcTimeoutMs);
                                bool shouldRetryStun = false;
                                bool needsFallback = false;
                                lock (localCts)
                                {
                                    if (!isConnected && !isFailed)
                                    {
                                        if (hostOnlyAttempt)
                                        {
                                            shouldRetryStun = true;
                                        }
                                        else
                                        {
                                            isFailed = true;
                                            needsFallback = true;
                                        }
                                    }
                                }

                                if (shouldRetryStun)
                                {
                                    Console.WriteLine("[WebSocketHandler] Host-only ICE timed out on LAN (2s). Retrying once with STUN candidates enabled.");
                                    hostOnlyAttempt = false;
                                    try
                                    {
                                        webRtcSession?.Dispose();
                                        webRtcSession = new WebRtcSessionService(
                                            onInputPacket: (data) => inputDispatcher.Dispatch(data),
                                            onIceCandidate: async (candidate) =>
                                            {
                                                await SendAsync(ws, new
                                                {
                                                    v = 1,
                                                    type = "webrtcIce",
                                                    candidate = candidate,
                                                    sdpMid = "0",
                                                    sdpMLineIndex = 0
                                                }, ct);
                                            },
                                            onConnected: () =>
                                            {
                                                lock (localCts)
                                                {
                                                    if (isConnected || isFailed) return;
                                                    isConnected = true;
                                                }
                                                Console.WriteLine("[WebSocketHandler] WebRTC connected after STUN retry.");
                                            },
                                            onFailed: async (reason) =>
                                            {
                                                lock (localCts)
                                                {
                                                    if (isFailed) return;
                                                    isFailed = true;
                                                }
                                                await HandleWebRtcFallbackAsync();
                                            },
                                            hostOnly: false
                                        );

                                        string retryAnswerSdp = await webRtcSession.ProcessOfferAndCreateAnswer(offerSdp);
                                        await SendAsync(ws, new { v = 1, type = "webrtcAnswer", sdp = retryAnswerSdp }, ct);

                                        await Task.Delay(3000);
                                        lock (localCts)
                                        {
                                            if (!isConnected && !isFailed)
                                            {
                                                isFailed = true;
                                                needsFallback = true;
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[WebSocketHandler] STUN retry failed: {ex.Message}");
                                        needsFallback = true;
                                    }
                                }

                                if (needsFallback)
                                {
                                    Console.WriteLine("[WebSocketHandler] WebRTC connection timeout. Falling back to JPEG.");
                                    await HandleWebRtcFallbackAsync();
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            await SendAsync(ws, new { v = 1, type = "webrtcDebugError", message = ex.ToString() }, ct);
                            await HandleWebRtcFallbackAsync();
                        }
                        break;

                    case "webrtcfailed":
                        // mobile sends webrtcFailed, normalized to lowercase here
                        await HandleWebRtcFallbackAsync();
                        break;

                    case "webrtcice":
                        if (!RequireAdmin()) break;
                        var clientCandidate = msg.GetStringOrNull("candidate");
                        var sdpMid = msg.GetStringOrNull("sdpMid");
                        var sdpMLineIndex = msg.GetIntOrDefault("sdpMLineIndex", 0);
                        if (clientCandidate != null)
                        {
                            webRtcSession?.AddIceCandidate(clientCandidate, sdpMid, sdpMLineIndex);
                        }
                        break;

                    // ── New: Screen capture ──
                    case "screencapturestart":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        if (_safe.DisableScreenCapture)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Screen capture disabled (safe mode)" }, ct);
                            break;
                        }
                        screenCapture?.Dispose();
                        var interval = msg.GetIntOrDefault("intervalMs", 1000);
                        var captureWidth = msg.GetIntOrDefault("width", 720);
                        var captureQuality = msg.GetIntOrDefault("quality", 65);
                        if (negotiatedScreenStream == ScreenStreamNegotiation.JpegBinV1)
                        {
                            screenCapture = new ScreenCaptureService((byte[] raw, int w, int h) =>
                            {
                                _ = SendBinaryFrameAsync(ws, raw, w, h, ct);
                            });
                        }
                        else
                        {
                            screenCapture = new ScreenCaptureService((b64, w, h) =>
                            {
                                _ = SendScreenFrameAsync(ws, b64, w, h, ct);
                            });
                        }
                        screenCapture.Start(interval, captureWidth, captureQuality);
                        _auditLog.Log(deviceName, "screenCaptureStart");
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    case "screencapturestop":
                        CleanupWebRtcResources();

                        screenCapture?.Dispose();
                        screenCapture = null;
                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;

                    // ── New: App list ──
                    case "getapplist":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var apps = AppListService.GetInstalledApps();
                        await SendAsync(ws, new
                        {
                            v = 1, type = "appList",
                            apps = apps.Select(a => new { name = a.Name, iconBase64 = a.IconBase64, exePath = a.ExePath }).ToList()
                        }, ct);
                        break;

                    // ── New: Custom commands ──
                    case "getcommands":
                        if (_safe.DisableCustomCommands)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Custom commands disabled (safe mode)" }, ct);
                            break;
                        }
                        _customCommands.Reload();
                        var cmds = _customCommands.GetCommands();
                        await SendAsync(ws, new
                        {
                            v = 1, type = "commandList",
                            commands = cmds.Select(c => new { label = c.Label, command = c.Command }).ToList()
                        }, ct);
                        break;

                    case "runcommand":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        if (_safe.DisableCustomCommands)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Custom commands disabled (safe mode)" }, ct);
                            break;
                        }
                        var cmdIdx = msg.GetIntOrDefault("index", -1);
                        if (cmdIdx < 0)
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = "Invalid index" }, ct);
                            break;
                        }

                        if (!TryConsumeMac($"runcommand|{cmdIdx}", out var macErrR))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = macErrR ?? "Command verification failed" }, ct);
                            break;
                        }

                        var cmdOk = _customCommands.RunCommand(cmdIdx);
                        _auditLog.Log(deviceName, $"runCommand:{cmdIdx}");
                        await SendAsync(ws, cmdOk
                            ? new { v = 1, type = "ok" }
                            : (object)new { v = 1, type = "error", message = "Command failed" }, ct);
                        break;

                    case "networkdiagnostics":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var flags = _networkBindingState?.Invoke() ?? (false, false);
                        var nd = NetworkDiagnostics.Collect(AgentRuntime.DefaultWsPort, AgentRuntime.DefaultDiscoveryPort, flags.WsServing, flags.DiscoveryUdp);
                        await SendAsync(ws, new
                        {
                            v = 1,
                            type = "networkDiagnostics",
                            lanIpv4 = nd.LanIpv4Candidates,
                            vpnOrTunnelLikely = nd.VpnOrTunnelLikely,
                            ipv6OnlyRisk = nd.Ipv6OnlyRisk,
                            webSocketPortInUse = nd.WebSocketPortConflict,
                            discoveryPortInUse = nd.DiscoveryPortConflict,
                            hints = nd.ActionHints,
                        }, ct);
                        break;

                    // ── New: Settings sync ──
                    case "settingssync":
                    {
                        var autoLock = msg.GetBoolOrDefault("autoLockOnDisconnect", false);
                        // Use lowercase boolean to match Dart's bool.toString() output
                        if (!TryConsumeMac($"settingsSync|{deviceId ?? ""}|{autoLock.ToString().ToLowerInvariant()}", out var macErrSt))
                        {
                            await SendAsync(ws, new { v = 1, type = "error", message = macErrSt ?? "Command verification failed" }, ct);
                            break;
                        }

                        if (deviceId is not null)
                        {
                            _paired.SetAutoLockOnDisconnect(deviceId, autoLock);
                        }

                        await SendAsync(ws, new { v = 1, type = "ok" }, ct);
                        break;
                    }

                    // ── New: Audit logs ──
                    case "getlogs":
                        if (!RequireAdmin()) { await SendRoleError(ws, ct); break; }
                        var logDate = msg.GetStringOrNull("date") ?? DateTimeOffset.Now.ToString("yyyy-MM-dd");
                        var logEntries = _auditLog.GetLogs(logDate);
                        await SendAsync(ws, new
                        {
                            v = 1, type = "logEntries",
                            entries = logEntries.Select(e => new { time = e.Time, device = e.Device, action = e.Action }).ToList()
                        }, ct);
                        break;

                    default:
                        // Gracefully ignore unknown types for forward-compatibility
                        break;
                }
            }
        }
        finally
        {
            KeyboardInjector.InputBlocked -= onInputBlocked;

            CleanupWebRtcResources();

            screenCapture?.Dispose();
            clipboardPollTimer?.Dispose();
            clipboardMonitor?.Dispose();
            notificationListener?.Stop();
            notificationListener = null;

            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch { /* ignore */ }

            if (authed && deviceId is not null)
            {
                _auditLog.Log(deviceName, "disconnected");
                _onDeviceDisconnected?.Invoke(deviceId);

                // Auto-lock on disconnect
                if (_paired.GetAutoLockOnDisconnect(deviceId))
                {
                    autoLockTimer = new System.Threading.Timer(_ =>
                    {
                        try { LockWorkStation(); } catch { /* ignore */ }
                    }, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

                    // Timer will self-dispose after firing once
                    _ = Task.Delay(TimeSpan.FromSeconds(12)).ContinueWith(_ => autoLockTimer?.Dispose());
                }
            }
        }
    }

    private static async Task SendRoleError(WebSocket ws, CancellationToken ct)
    {
        await SendAsync(ws, new { v = 1, type = "error", message = "Insufficient permissions for this action" }, ct);
    }

    private const int MaxMessageBytes = 10 * 1024 * 1024; // 10 MB hard limit

    private static async Task<Dictionary<string, JsonElement>?> ReceiveJsonAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[256 * 1024];
        using var ms = new MemoryStream();

        while (true)
        {
            WebSocketReceiveResult result;
            try { result = await ws.ReceiveAsync(buffer, ct); }
            catch { return null; }

            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);

            if (ms.Length > MaxMessageBytes)
            {
                // Oversized message — close the connection to prevent OOM.
                try { await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None); } catch { }
                return null;
            }

            if (result.EndOfMessage) break;
        }

        var rawBytes = ms.ToArray();
        if (rawBytes.Length >= 21 && rawBytes[0] == 0x02)
        {
            var trId = new Guid(rawBytes.AsSpan(1, 16)).ToString();
            var chIdx = BinaryPrimitives.ReadInt32BigEndian(rawBytes.AsSpan(17, 4));
            var dataB64 = Convert.ToBase64String(rawBytes, 21, rawBytes.Length - 21);

            return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["type"] = JsonDocument.Parse("\"filetransferchunk\"").RootElement,
                ["id"] = JsonDocument.Parse($"\"{trId}\"").RootElement,
                ["chunkIndex"] = JsonDocument.Parse(chIdx.ToString(CultureInfo.InvariantCulture)).RootElement,
                ["data"] = JsonDocument.Parse($"\"{dataB64}\"").RootElement
            };
        }

        var json = Encoding.UTF8.GetString(rawBytes);
        try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json); }
        catch
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["type"] = JsonDocument.Parse("\"invalid\"").RootElement
            };
        }
    }

    private static Task SendAsync(WebSocket ws, object obj, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        return ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    /// <summary>
    /// Screen frames are large; avoid JSON serializer overhead and do not block the capture thread.
    /// </summary>
    private static Task SendScreenFrameAsync(WebSocket ws, string b64, int width, int height, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try
            {
                if (ws.State != WebSocketState.Open) return;
                var json = string.Concat(
                    "{\"v\":1,\"type\":\"screenFrame\",\"data\":\"",
                    b64,
                    "\",\"width\":",
                    width.ToString(CultureInfo.InvariantCulture),
                    ",\"height\":",
                    height.ToString(CultureInfo.InvariantCulture),
                    "}");
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            }
            catch { /* connection may have closed */ }
        }, ct);
    }

    /// <summary>
    /// Sends a screen frame as a binary WebSocket message using the jpeg-bin-v1 wire format.
    /// Header: [0] 0x01 (screen frame) | [1-4] width uint32 BE | [5-8] height uint32 BE | [9+] raw JPEG bytes.
    /// </summary>
    internal static Task SendBinaryFrameAsync(WebSocket ws, byte[] jpegBytes, int width, int height, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            try
            {
                if (ws.State != WebSocketState.Open) return;
                var buffer = new byte[9 + jpegBytes.Length];
                buffer[0] = 0x01; // message type: screen frame
                BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(1, 4), (uint)width);
                BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(5, 4), (uint)height);
                Buffer.BlockCopy(jpegBytes, 0, buffer, 9, jpegBytes.Length);
                await ws.SendAsync(buffer, WebSocketMessageType.Binary, true, ct);
            }
            catch { /* connection may have closed */ }
        }, ct);
    }

    private static void ProcessCpuFrame(byte[] bgraBytes, int srcWidth, int srcHeight, byte[] destBytes, int destWidth, int destHeight)
    {
        try
        {
            var srcHandle = GCHandle.Alloc(bgraBytes, GCHandleType.Pinned);
            var destHandle = GCHandle.Alloc(destBytes, GCHandleType.Pinned);
            try
            {
                using (var srcBmp = new Bitmap(
                    srcWidth,
                    srcHeight,
                    srcWidth * 4,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb,
                    srcHandle.AddrOfPinnedObject()))
                using (var destBmp = new Bitmap(
                    destWidth,
                    destHeight,
                    destWidth * 4,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb,
                    destHandle.AddrOfPinnedObject()))
                {
                    using (var g = Graphics.FromImage(destBmp))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        g.DrawImage(srcBmp, 0, 0, destWidth, destHeight);

                        double scaleX = (double)destWidth / srcWidth;
                        double scaleY = (double)destHeight / srcHeight;
                        ScreenCaptureService.DrawCursorOnto(g, new Rectangle(0, 0, srcWidth, srcHeight), scaleX, scaleY);
                    }
                }
            }
            finally
            {
                srcHandle.Free();
                destHandle.Free();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WebSocketHandler] Failed to process CPU frame: {ex.Message}");
        }
    }
}

internal static class JsonDictExtensions
{
    public static string? GetStringOrNull(this Dictionary<string, JsonElement> dict, string key)
    {
        return dict.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    public static int GetIntOrDefault(this Dictionary<string, JsonElement> dict, string key, int fallback)
    {
        if (!dict.TryGetValue(key, out var el)) return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var v) => v,
            _ => fallback,
        };
    }

    public static long GetLongOrDefault(this Dictionary<string, JsonElement> dict, string key, long fallback)
    {
        if (!dict.TryGetValue(key, out var el)) return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var v) => v,
            _ => fallback,
        };
    }

    public static double GetDoubleOrDefault(this Dictionary<string, JsonElement> dict, string key, double fallback)
    {
        if (!dict.TryGetValue(key, out var el)) return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var v) => v,
            _ => fallback,
        };
    }

    public static bool GetBoolOrDefault(this Dictionary<string, JsonElement> dict, string key, bool fallback)
    {
        if (!dict.TryGetValue(key, out var el)) return fallback;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    public static List<string>? GetStringArrayOrNull(this Dictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
        }
        return list;
    }
}
