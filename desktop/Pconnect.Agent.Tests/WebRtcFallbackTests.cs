using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Pconnect.Agent.Services;
using Xunit;

namespace Pconnect.Agent.Tests;

public sealed class WebRtcFallbackTests
{
    [Fact]
    public void InputDispatcher_ignores_too_short_packets()
    {
        var injector = new KeyboardInjector();
        var dispatcher = new InputDispatcher(injector);

        // Should return early and not throw
        dispatcher.Dispatch(new byte[] { 0x01, 0x00, 0x00 });
        dispatcher.Dispatch(Array.Empty<byte>());
    }

    [Fact]
    public void InputDispatcher_ignores_unknown_event_types()
    {
        var injector = new KeyboardInjector();
        var dispatcher = new InputDispatcher(injector);

        var packet = new byte[10];
        packet[0] = 0x99; // Unknown event type
        dispatcher.Dispatch(packet); // Should not crash or call SendInput
    }

    [Fact]
    public void InputDispatcher_parses_mouse_move_packet_safely()
    {
        var injector = new KeyboardInjector();
        var dispatcher = new InputDispatcher(injector);

        // Event type 0x01 (move), x=0, y=0, extra=0
        var packet = new byte[10];
        packet[0] = 0x01;
        dispatcher.Dispatch(packet); // dx=0, dy=0 returns early in KeyboardInjector, no SendInput
    }

    [Fact]
    public void InputDispatcher_ignores_unknown_mouse_buttons()
    {
        var injector = new KeyboardInjector();
        var dispatcher = new InputDispatcher(injector);

        // Event type 0x02 (button down), extra=9 (unknown button)
        var packet = new byte[10];
        packet[0] = 0x02;
        packet[9] = 0x09;
        dispatcher.Dispatch(packet); // Should do nothing
    }

    [Fact]
    public async Task WebSocketHandler_webrtcOffer_reaches_pipeline()
    {
        ScreenCaptureDxgi.ForceSupported = true;
        ScreenCaptureDxgi.ForceInitializeSuccess = true;
        H264EncoderService.ForceInitializeSuccess = true;
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new PairedDevicesStore(tempFile);
            var pairing = new PairingService();
            var pc = new PcActions();
            var ui = new FakeUiActions();
            
            var token = store.PairNewDevice("test-device-id", "Test Phone");
            var handler = new WebSocketHandler(pairing, store, pc, ui);
            
            var socket = new FakeWebSocket();
            var cts = new CancellationTokenSource();

            // Queue hello message
            socket.EnqueueInput(JsonSerializer.Serialize(new
            {
                v = 1,
                type = "hello",
                clientVersion = "1.0.0",
                proto = 2,
                deviceId = "test-device-id",
                token = token,
                screenStreamModes = new[] { "webrtc-v1" }
            }));

            // Queue webrtcOffer message (camelCase wire format)
            socket.EnqueueInput(JsonSerializer.Serialize(new
            {
                v = 1,
                type = "webrtcOffer",
                sdp = "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\nc=IN IP4 127.0.0.1\r\na=setup:actpass\r\na=mid:0\r\na=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF\r\na=rtpmap:102 H264/90000"
            }));

            var connectionTask = handler.HandleConnectionAsync(socket, System.Net.IPAddress.Loopback, cts.Token);
            
            // Let the handler run to process input messages
            await Task.WhenAny(connectionTask, Task.Delay(500));
            cts.Cancel();

            // Assert that webrtcAnswer was returned with correct camelCase wire format
            var foundAnswer = false;
            foreach (var sentBytes in socket.SentMessages)
            {
                var text = Encoding.UTF8.GetString(sentBytes);
                if (text.Contains("\"type\":\"webrtcAnswer\""))
                {
                    foundAnswer = true;
                }
            }
            var messagesStr = string.Join("; ", socket.SentMessages.Select(b => Encoding.UTF8.GetString(b)));
            Assert.True(foundAnswer, $"Expected to find literal 'webrtcAnswer' camelCase response in socket messages. Got: {messagesStr}");
        }
        finally
        {
            ScreenCaptureDxgi.ForceSupported = null;
            ScreenCaptureDxgi.ForceInitializeSuccess = false;
            H264EncoderService.ForceInitializeSuccess = false;
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WebSocketHandler_webrtcFallback_triggers_on_timeout()
    {
        ScreenCaptureDxgi.ForceSupported = true;
        ScreenCaptureDxgi.ForceInitializeSuccess = true;
        H264EncoderService.ForceInitializeSuccess = true;
        var tempFile = Path.GetTempFileName();
        try
        {
            var store = new PairedDevicesStore(tempFile);
            var pairing = new PairingService();
            var pc = new PcActions();
            var ui = new FakeUiActions();
            
            var token = store.PairNewDevice("test-device-id", "Test Phone");
            var handler = new WebSocketHandler(pairing, store, pc, ui)
            {
                WebRtcTimeoutMs = 10 // Set short timeout for instant trigger
            };
            
            var socket = new FakeWebSocket();
            var cts = new CancellationTokenSource();

            // Queue hello message
            socket.EnqueueInput(JsonSerializer.Serialize(new
            {
                v = 1,
                type = "hello",
                clientVersion = "1.0.0",
                proto = 2,
                deviceId = "test-device-id",
                token = token,
                screenStreamModes = new[] { "webrtc-v1" }
            }));

            // Queue webrtcOffer message (camelCase wire format)
            socket.EnqueueInput(JsonSerializer.Serialize(new
            {
                v = 1,
                type = "webrtcOffer",
                sdp = "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\nm=video 9 UDP/TLS/RTP/SAVPF 102\r\nc=IN IP4 127.0.0.1\r\na=setup:actpass\r\na=mid:0\r\na=fingerprint:sha-256 00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF\r\na=rtpmap:102 H264/90000"
            }));

            var connectionTask = handler.HandleConnectionAsync(socket, System.Net.IPAddress.Loopback, cts.Token);
            
            // Allow timeout to fire and trigger fallback
            await Task.WhenAny(connectionTask, Task.Delay(1000));
            cts.Cancel();

            // Assert that webrtcFallback was returned with correct camelCase and Jpeg mode
            var foundFallback = false;
            foreach (var sentBytes in socket.SentMessages)
            {
                var text = Encoding.UTF8.GetString(sentBytes);
                if (text.Contains("\"type\":\"webrtcFallback\"") && text.Contains("\"mode\":\"jpeg-v1\""))
                {
                    foundFallback = true;
                }
            }
            Assert.True(foundFallback, "Expected to find literal 'webrtcFallback' with mode 'jpeg-v1' response in socket messages.");
        }
        finally
        {
            ScreenCaptureDxgi.ForceSupported = null;
            ScreenCaptureDxgi.ForceInitializeSuccess = false;
            H264EncoderService.ForceInitializeSuccess = false;
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void H264EncoderService_does_not_perform_cpu_readback_in_gpu_path()
    {
        var baseDir = AppContext.BaseDirectory;
        var file = Path.Combine(baseDir, "../../../../Pconnect.Agent/Services/H264EncoderService.cs");
        if (File.Exists(file))
        {
            var content = File.ReadAllText(file);
            
            Assert.Contains("EncodeGpuTexture", content);
            
            var startIdx = content.IndexOf("public ReadOnlyMemory<byte> EncodeGpuTexture");
            var endIdx = content.IndexOf("private static byte[] ConvertBgraToNv12");
            Assert.True(startIdx > 0, "EncodeGpuTexture not found in file");
            Assert.True(endIdx > startIdx, "ConvertBgraToNv12 not found after EncodeGpuTexture");
            
            var gpuPathBody = content.Substring(startIdx, endIdx - startIdx);
            
            // Verify no staging copies or mapping back to CPU
            Assert.DoesNotContain(".Map", gpuPathBody);
            Assert.DoesNotContain(".Unmap", gpuPathBody);
            Assert.DoesNotContain("CopyFrameToCpu", gpuPathBody);
            Assert.DoesNotContain("ConvertBgraToNv12", gpuPathBody);
        }
    }
}

internal sealed class FakeUiActions : IUiActions
{
    public void ShowAgentUi() { }
}

internal sealed class FakeWebSocket : WebSocket
{
    private readonly Queue<byte[]> _inputQueue = new();
    private readonly List<byte[]> _sentMessages = new();
    private WebSocketState _state = WebSocketState.Open;

    public List<byte[]> SentMessages => _sentMessages;

    public void EnqueueInput(string text)
    {
        _inputQueue.Enqueue(Encoding.UTF8.GetBytes(text));
    }

    public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;
    public override string? CloseStatusDescription => "Normal";
    public override WebSocketState State => _state;
    public override string? SubProtocol => null;

    public override void Abort() => _state = WebSocketState.Aborted;

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override void Dispose() { }

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        if (_state != WebSocketState.Open)
        {
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        if (_inputQueue.Count == 0)
        {
            _state = WebSocketState.Closed;
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        }

        var msg = _inputQueue.Dequeue();
        var count = Math.Min(buffer.Count, msg.Length);
        Array.Copy(msg, 0, buffer.Array!, buffer.Offset, count);

        return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Text, true));
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        var bytes = new byte[buffer.Count];
        Array.Copy(buffer.Array!, buffer.Offset, bytes, 0, buffer.Count);
        _sentMessages.Add(bytes);
        return Task.CompletedTask;
    }
}
