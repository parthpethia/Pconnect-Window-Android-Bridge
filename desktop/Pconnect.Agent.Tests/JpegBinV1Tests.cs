using System;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Pconnect.Agent.Services;
using Xunit;

namespace Pconnect.Agent.Tests;

public sealed class JpegBinV1Tests
{
    [Fact]
    public async Task SendBinaryFrameAsync_produces_correct_header_and_payload()
    {
        var socket = new FakeWebSocket();
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        int width = 1920;
        int height = 1080;

        await WebSocketHandler.SendBinaryFrameAsync(socket, jpegBytes, width, height, CancellationToken.None);

        Assert.Single(socket.SentMessages);
        var frame = socket.SentMessages[0];

        // Total size: 9-byte header + payload
        Assert.Equal(9 + jpegBytes.Length, frame.Length);

        // Byte 0: message type 0x01
        Assert.Equal(0x01, frame[0]);

        // Bytes 1-4: width as uint32 big-endian
        var parsedWidth = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(1, 4));
        Assert.Equal((uint)width, parsedWidth);

        // Bytes 5-8: height as uint32 big-endian
        var parsedHeight = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(5, 4));
        Assert.Equal((uint)height, parsedHeight);

        // Bytes 9+: raw JPEG payload matches exactly
        var payload = frame.AsSpan(9).ToArray();
        Assert.Equal(jpegBytes, payload);
    }

    [Fact]
    public async Task SendBinaryFrameAsync_various_dimensions()
    {
        var testCases = new[]
        {
            (width: 720, height: 405),
            (width: 1280, height: 720),
            (width: 3840, height: 2160),
            (width: 1, height: 1),
        };

        foreach (var (width, height) in testCases)
        {
            var socket = new FakeWebSocket();
            var jpegBytes = new byte[64];
            new Random(42).NextBytes(jpegBytes);

            await WebSocketHandler.SendBinaryFrameAsync(socket, jpegBytes, width, height, CancellationToken.None);

            Assert.Single(socket.SentMessages);
            var frame = socket.SentMessages[0];

            Assert.Equal(0x01, frame[0]);
            Assert.Equal((uint)width, BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(1, 4)));
            Assert.Equal((uint)height, BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(5, 4)));
            Assert.Equal(jpegBytes, frame.AsSpan(9).ToArray());
        }
    }

    [Fact]
    public async Task SendBinaryFrameAsync_sends_as_binary_message_type()
    {
        var socket = new BinaryTrackingWebSocket();
        var jpegBytes = new byte[] { 0xFF, 0xD8 };

        await WebSocketHandler.SendBinaryFrameAsync(socket, jpegBytes, 100, 50, CancellationToken.None);

        Assert.Single(socket.MessageTypes);
        Assert.Equal(WebSocketMessageType.Binary, socket.MessageTypes[0]);
    }

    [Fact]
    public async Task SendBinaryFrameAsync_empty_payload()
    {
        var socket = new FakeWebSocket();
        var jpegBytes = Array.Empty<byte>();

        await WebSocketHandler.SendBinaryFrameAsync(socket, jpegBytes, 640, 480, CancellationToken.None);

        Assert.Single(socket.SentMessages);
        var frame = socket.SentMessages[0];

        Assert.Equal(9, frame.Length); // header only
        Assert.Equal(0x01, frame[0]);
        Assert.Equal(640u, BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(1, 4)));
        Assert.Equal(480u, BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(5, 4)));
    }
}

/// <summary>
/// Minimal WebSocket mock that tracks message types sent.
/// </summary>
internal sealed class BinaryTrackingWebSocket : WebSocket
{
    public System.Collections.Generic.List<WebSocketMessageType> MessageTypes { get; } = new();
    public System.Collections.Generic.List<byte[]> SentMessages { get; } = new();

    public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;
    public override string? CloseStatusDescription => "Normal";
    public override WebSocketState State => WebSocketState.Open;
    public override string? SubProtocol => null;

    public override void Abort() { }
    public override Task CloseAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask;
    public override Task CloseOutputAsync(WebSocketCloseStatus s, string? d, CancellationToken c) => Task.CompletedTask;
    public override void Dispose() { }

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct)
    {
        MessageTypes.Add(messageType);
        var bytes = new byte[buffer.Count];
        Array.Copy(buffer.Array!, buffer.Offset, bytes, 0, buffer.Count);
        SentMessages.Add(bytes);
        return Task.CompletedTask;
    }
}
