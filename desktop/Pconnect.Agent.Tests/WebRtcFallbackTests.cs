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
using System.Runtime.InteropServices;

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
            var endIdx = content.IndexOf("private static void ConvertBgraToNv12");
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

    [Fact]
    public void ProfileGpuCompositionDelta()
    {
        int hr = D3D11CreateDevice(
            IntPtr.Zero,
            1, // D3D_DRIVER_TYPE_HARDWARE
            IntPtr.Zero,
            0x20, // BGRA support
            IntPtr.Zero,
            0,
            7, // SDK version
            out IntPtr device,
            out _,
            out IntPtr context);

        string deviceType = "Hardware";
        if (hr != 0)
        {
            // Fall back to WARP
            hr = D3D11CreateDevice(
                IntPtr.Zero,
                5, // D3D_DRIVER_TYPE_WARP
                IntPtr.Zero,
                0x20, // BGRA support
                IntPtr.Zero,
                0,
                7, // SDK version
                out device,
                out _,
                out context);
            deviceType = "WARP";
        }

        if (hr != 0)
        {
            File.WriteAllText("gpu_profile_results.txt", $"Failed to create D3D11 device (Hardware & WARP). HR = 0x{hr:X}");
            return;
        }

        try
        {
            // Create textures
            var srcDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = 1920,
                Height = 1080,
                MipLevels = 1,
                ArraySize = 1,
                Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = 0,
                BindFlags = 0x20 | 0x8,
                CPUAccessFlags = 0,
                MiscFlags = 0
            };

            var dstDesc = srcDesc;
            dstDesc.MiscFlags = Direct3DConstants.D3D11_RESOURCE_MISC_GDI_COMPATIBLE;

            var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
                GetVtableFunc(device, 5));

            int hrSrc = createTexture2D(device, ref srcDesc, IntPtr.Zero, out IntPtr srcTexture);
            int hrDst = createTexture2D(device, ref dstDesc, IntPtr.Zero, out IntPtr dstTexture);

            if (hrSrc != 0 || hrDst != 0)
            {
                File.WriteAllText("gpu_profile_results.txt", $"Failed to create textures. DeviceType={deviceType}, hrSrc=0x{hrSrc:X}, hrDst=0x{hrDst:X}");
                if (srcTexture != IntPtr.Zero) Marshal.Release(srcTexture);
                if (dstTexture != IntPtr.Zero) Marshal.Release(dstTexture);
                return;
            }
            try
            {
                Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                Guid IID_IDXGISurface = new Guid("896b2801-774e-4d71-92e7-d0d93e940240");
                Guid IID_IDXGISurface1 = new Guid("4ae63092-6327-4c1b-80ae-bfe12ea32b86");

                int hrTex = Marshal.QueryInterface(dstTexture, ref IID_ID3D11Texture2D, out IntPtr texPtr);
                int hrSurfBase = Marshal.QueryInterface(dstTexture, ref IID_IDXGISurface, out IntPtr dxgiSurfaceBasePtr);
                int hrSurf = Marshal.QueryInterface(dstTexture, ref IID_IDXGISurface1, out IntPtr dxgiSurfacePtr);

                if (texPtr != IntPtr.Zero) Marshal.Release(texPtr);
                if (dxgiSurfaceBasePtr != IntPtr.Zero) Marshal.Release(dxgiSurfaceBasePtr);

                if (hrSurf != 0 || dxgiSurfacePtr == IntPtr.Zero)
                {
                    File.WriteAllText("gpu_profile_results.txt", 
                        $"Failed to query IDXGISurface1. DeviceType={deviceType}, hrTex = 0x{hrTex:X}, hrSurfBase = 0x{hrSurfBase:X}, hrSurf = 0x{hrSurf:X}");
                    return;
                }
                var copyResource = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(
                    GetVtableFunc(context, 47));

                int iterations = 100;
                var sw = new System.Diagnostics.Stopwatch();

                // 1. Baseline: Copy resource only
                sw.Start();
                for (int i = 0; i < iterations; i++)
                {
                    copyResource(context, dstTexture, srcTexture);
                }
                sw.Stop();
                double avgBaseTime = sw.Elapsed.TotalMilliseconds / iterations;

                // 2. Composition: Copy resource + GDI overlay
                double avgCompTime = 0;

                try
                {
                    var getDC = Marshal.GetDelegateForFunctionPointer<GetDCDelegate>(
                        GetVtableFunc(dxgiSurfacePtr, 11));
                    var releaseDC = Marshal.GetDelegateForFunctionPointer<ReleaseDCDelegate>(
                        GetVtableFunc(dxgiSurfacePtr, 12));

                    // Warm up
                    copyResource(context, dstTexture, srcTexture);
                    int hrDcWarm = getDC(dxgiSurfacePtr, false, out IntPtr hdcWarm);
                    if (hrDcWarm != 0 || hdcWarm == IntPtr.Zero)
                    {
                        File.WriteAllText("gpu_profile_results.txt", $"Failed GetDC warmup: 0x{hrDcWarm:X}");
                        return;
                    }
                    try
                    {
                        using (var g = System.Drawing.Graphics.FromHdc(hdcWarm))
                        {
                            ScreenCaptureService.DrawCursorOnto(g, new System.Drawing.Rectangle(0, 0, 1920, 1080), 1.0, 1.0);
                        }
                    }
                    finally
                    {
                        releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                    }

                    sw.Reset();
                    sw.Start();
                    for (int i = 0; i < iterations; i++)
                    {
                        copyResource(context, dstTexture, srcTexture);
                        if (getDC(dxgiSurfacePtr, false, out IntPtr hdc) == 0 && hdc != IntPtr.Zero)
                        {
                            try
                            {
                                using (var g = System.Drawing.Graphics.FromHdc(hdc))
                                {
                                    ScreenCaptureService.DrawCursorOnto(g, new System.Drawing.Rectangle(0, 0, 1920, 1080), 1.0, 1.0);
                                }
                            }
                            finally
                            {
                                releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                            }
                        }
                    }
                    sw.Stop();
                    avgCompTime = sw.Elapsed.TotalMilliseconds / iterations;
                }
                finally
                {
                    Marshal.Release(dxgiSurfacePtr);
                }

                double delta = avgCompTime - avgBaseTime;
                var results = $"GPU Cursor Composition Profiling (1080p, WARP/Hardware device):\n" +
                              $"Avg time per frame (Base Copy Only): {avgBaseTime:F4} ms\n" +
                              $"Avg time per frame (With GDI Cursor Composition): {avgCompTime:F4} ms\n" +
                              $"Delta Overhead per frame: {delta:F4} ms\n" +
                              $"Estimated CPU/GPU overhead: {(delta / avgCompTime) * 100:F1}%\n";

                File.WriteAllText("gpu_profile_results.txt", results);
            }
            finally
            {
                Marshal.Release(srcTexture);
                Marshal.Release(dstTexture);
            }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(device);
        }
    }

    [Fact]
    public void ProfileSustainedThroughputAndJitter()
    {
        int hr = D3D11CreateDevice(
            IntPtr.Zero,
            1, // D3D_DRIVER_TYPE_HARDWARE
            IntPtr.Zero,
            0x20, // BGRA support
            IntPtr.Zero,
            0,
            7, // SDK version
            out IntPtr device,
            out _,
            out IntPtr context);

        string deviceType = "Hardware";
        if (hr != 0)
        {
            // Fall back to WARP
            hr = D3D11CreateDevice(
                IntPtr.Zero,
                5, // D3D_DRIVER_TYPE_WARP
                IntPtr.Zero,
                0x20, // BGRA support
                IntPtr.Zero,
                0,
                7, // SDK version
                out device,
                out _,
                out context);
            deviceType = "WARP";
        }

        if (hr != 0)
        {
            File.WriteAllText("live_streaming_profile_results.txt", $"Failed to create D3D11 device: 0x{hr:X}");
            return;
        }

        try
        {
            var srcDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = 1920,
                Height = 1080,
                MipLevels = 1,
                ArraySize = 1,
                Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = 0,
                BindFlags = 0x20 | 0x8,
                CPUAccessFlags = 0,
                MiscFlags = 0
            };

            var dstDesc = srcDesc;
            dstDesc.MiscFlags = Direct3DConstants.D3D11_RESOURCE_MISC_GDI_COMPATIBLE;

            var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
                GetVtableFunc(device, 5));

            int hrSrc = createTexture2D(device, ref srcDesc, IntPtr.Zero, out IntPtr srcTexture);
            int hrDst = createTexture2D(device, ref dstDesc, IntPtr.Zero, out IntPtr dstTexture);

            if (hrSrc != 0 || hrDst != 0)
            {
                File.WriteAllText("live_streaming_profile_results.txt", $"Failed to create textures: hrSrc=0x{hrSrc:X}, hrDst=0x{hrDst:X}");
                if (srcTexture != IntPtr.Zero) Marshal.Release(srcTexture);
                if (dstTexture != IntPtr.Zero) Marshal.Release(dstTexture);
                return;
            }

            try
            {
                Guid IID_IDXGISurface1 = new Guid("4ae63092-6327-4c1b-80ae-bfe12ea32b86");
                int hrSurf = Marshal.QueryInterface(dstTexture, ref IID_IDXGISurface1, out IntPtr dxgiSurfacePtr);
                if (hrSurf != 0 || dxgiSurfacePtr == IntPtr.Zero)
                {
                    File.WriteAllText("live_streaming_profile_results.txt", $"Failed to query IDXGISurface1: 0x{hrSurf:X}");
                    return;
                }

                try
                {
                    var copyResource = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(
                        GetVtableFunc(context, 47));
                    var getDC = Marshal.GetDelegateForFunctionPointer<GetDCDelegate>(
                        GetVtableFunc(dxgiSurfacePtr, 11));
                    var releaseDC = Marshal.GetDelegateForFunctionPointer<ReleaseDCDelegate>(
                        GetVtableFunc(dxgiSurfacePtr, 12));

                    int iterations = 300;

                    // Warm-up GDI
                    copyResource(context, dstTexture, srcTexture);
                    if (getDC(dxgiSurfacePtr, false, out IntPtr hdcWarm) == 0 && hdcWarm != IntPtr.Zero)
                    {
                        try
                        {
                            using (var g = System.Drawing.Graphics.FromHdc(hdcWarm))
                            {
                                ScreenCaptureService.DrawCursorOnto(g, new System.Drawing.Rectangle(0, 0, 1920, 1080), 1.0, 1.0);
                            }
                        }
                        finally
                        {
                            releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                        }
                    }

                    // Scenario 1: Async GPU Copy (CopyResource only)
                    var s1Times = new List<double>();
                    for (int i = 0; i < iterations; i++)
                    {
                        long tStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        copyResource(context, dstTexture, srcTexture);
                        long tEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                        s1Times.Add((double)(tEnd - tStart) * 1000 / System.Diagnostics.Stopwatch.Frequency);
                    }

                    // Scenario 2: Forced CPU-GPU Sync (CopyResource + GetDC + ReleaseDC without drawing)
                    var s2Times = new List<double>();
                    for (int i = 0; i < iterations; i++)
                    {
                        long tStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        copyResource(context, dstTexture, srcTexture);
                        int hrDc = getDC(dxgiSurfacePtr, false, out IntPtr hdcSync);
                        if (hrDc == 0 && hdcSync != IntPtr.Zero)
                        {
                            releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                        }
                        long tEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                        s2Times.Add((double)(tEnd - tStart) * 1000 / System.Diagnostics.Stopwatch.Frequency);
                    }

                    // Scenario 3: Full GDI Composition (Copy + GetDC + Draw + ReleaseDC)
                    var s3Times = new List<double>();
                    for (int i = 0; i < iterations; i++)
                    {
                        long tStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        copyResource(context, dstTexture, srcTexture);
                        int hrDc = getDC(dxgiSurfacePtr, false, out IntPtr hdc);
                        if (hrDc == 0 && hdc != IntPtr.Zero)
                        {
                            try
                            {
                                using (var g = System.Drawing.Graphics.FromHdc(hdc))
                                {
                                    ScreenCaptureService.DrawCursorOnto(g, new System.Drawing.Rectangle(0, 0, 1920, 1080), 1.0, 1.0);
                                }
                            }
                            finally
                            {
                                releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                            }
                        }
                        long tEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                        s3Times.Add((double)(tEnd - tStart) * 1000 / System.Diagnostics.Stopwatch.Frequency);
                    }

                    double avgS1 = s1Times.Average();
                    double maxS1 = s1Times.Max();
                    double avgS2 = s2Times.Average();
                    double maxS2 = s2Times.Max();
                    double avgS3 = s3Times.Average();
                    double maxS3 = s3Times.Max();

                    double syncStallCost = avgS2 - avgS1;
                    double gdiDrawCost = avgS3 - avgS2;

                    double maxFpsLimit = 1000.0 / avgS3;

                    var results = $"Sustained GPU/GDI Composition Throughput & Jitter Profiling (1080p, device={deviceType}, {iterations} iterations):\n" +
                                  $"Scenario 1: Async GPU Copy Only (No Sync)\n" +
                                  $"  - Avg Frame Time: {avgS1:F4} ms\n" +
                                  $"  - Max Frame Time: {maxS1:F4} ms\n" +
                                  $"Scenario 2: Forced CPU-GPU Sync (GetDC/ReleaseDC, No Drawing)\n" +
                                  $"  - Avg Frame Time: {avgS2:F4} ms\n" +
                                  $"  - Max Frame Time: {maxS2:F4} ms\n" +
                                  $"Scenario 3: Full GDI Composition (Copy + GetDC + Draw + ReleaseDC)\n" +
                                  $"  - Avg Frame Time: {avgS3:F4} ms\n" +
                                  $"  - Max Frame Time: {maxS3:F4} ms\n" +
                                  $"Overhead Breakdown:\n" +
                                  $"  - CPU-GPU Synchronization Stall: {syncStallCost:F4} ms\n" +
                                  $"  - GDI Mouse Cursor Drawing Cost: {gdiDrawCost:F4} ms\n" +
                                  $"  - Total Delta Overhead: {avgS3 - avgS1:F4} ms\n" +
                                  $"  - Theoretical Max Throughput: {maxFpsLimit:F1} FPS\n" +
                                  $"  - Jitter (Worst-case Frame Time): {maxS3:F4} ms\n";

                    File.WriteAllText("live_streaming_profile_results.txt", results);
                }
                finally
                {
                    Marshal.Release(dxgiSurfacePtr);
                }
            }
            finally
            {
                Marshal.Release(srcTexture);
                Marshal.Release(dstTexture);
            }
        }
        finally
        {
            Marshal.Release(context);
            Marshal.Release(device);
        }
    }

    [Fact]
    public void TestGpuEncoderCursorCompositionDirectly()
    {
        if (!ScreenCaptureDxgi.IsSupported())
        {
            File.WriteAllText("gpu_encode_test_status.txt", "DXGI not supported on this host.");
            return;
        }

        using var dxgi = new ScreenCaptureDxgi();
        using var encoder = new H264EncoderService();
        int w = (int)dxgi.Width;
        int h = (int)dxgi.Height;
        try
        {
            encoder.Initialize(w, h, 30, 2500, dxgi.Device);
        }
        catch (Exception ex)
        {
            File.WriteAllText("gpu_encode_test_status.txt", $"Skipping test: Media Foundation initialization failed: {ex.Message}");
            return;
        }

        DxgiFrame? frame = null;
        for (int i = 0; i < 20; i++)
        {
            frame = dxgi.AcquireNextFrame(100);
            if (frame != null) break;
            Thread.Sleep(50);
        }

        if (frame == null)
        {
            File.WriteAllText("gpu_encode_test_status.txt", "Could not acquire screen frame.");
            return;
        }

        try
        {
            var nals = encoder.EncodeGpuTexture(frame.Texture, w, h, false);
            File.WriteAllText("gpu_encode_test_status.txt", $"Encode succeeded. Nals length: {nals.Length}");
        }
        catch (Exception ex)
        {
            File.WriteAllText("gpu_encode_test_status.txt", $"Encode failed: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            frame.Release();
        }
    }

    [Fact]
    public void VerifyGpuCursorVisualComposition()
    {
        if (!ScreenCaptureDxgi.IsSupported())
        {
            File.WriteAllText("cursor_visual_status.txt", "DXGI not supported on this host.");
            return;
        }

        using var dxgi = new ScreenCaptureDxgi();
        int w = (int)dxgi.Width;
        int h = (int)dxgi.Height;

        DxgiFrame? frame = null;
        for (int i = 0; i < 20; i++)
        {
            frame = dxgi.AcquireNextFrame(100);
            if (frame != null) break;
            Thread.Sleep(50);
        }

        if (frame == null)
        {
            File.WriteAllText("cursor_visual_status.txt", "Could not acquire screen frame.");
            return;
        }

        IntPtr dstTexture = IntPtr.Zero;
        try
        {
            var device = dxgi.Device;
            var context = dxgi.Context;

            // Create GDI-compatible destination texture
            var dstDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = 0,
                BindFlags = 0x20 | 0x8,
                CPUAccessFlags = 0,
                MiscFlags = Direct3DConstants.D3D11_RESOURCE_MISC_GDI_COMPATIBLE
            };

            var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
                GetVtableFunc(device, 5));

            int hrDst = createTexture2D(device, ref dstDesc, IntPtr.Zero, out dstTexture);
            if (hrDst != 0 || dstTexture == IntPtr.Zero)
            {
                File.WriteAllText("cursor_visual_status.txt", $"Failed to create GDI compatible texture: 0x{hrDst:X}");
                return;
            }

            // Copy captured frame to GDI-compatible texture
            var copyResource = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(
                GetVtableFunc(context, 47));
            copyResource(context, dstTexture, frame.Texture);

            // Release frame immediately to free DXGI resources
            frame.Release();
            frame = null;

            // Flush context to ensure copy is completed before GDI access
            var flush = Marshal.GetDelegateForFunctionPointer<FlushDelegate>(
                GetVtableFunc(context, 110)); // Index 110 is Flush
            flush(context);

            // Draw cursor on the GDI-compatible texture
            Guid IID_IDXGISurface1 = new Guid("4ae63092-6327-4c1b-80ae-bfe12ea32b86");
            int hrSurf = Marshal.QueryInterface(dstTexture, ref IID_IDXGISurface1, out IntPtr dxgiSurfacePtr);
            if (hrSurf != 0 || dxgiSurfacePtr == IntPtr.Zero)
            {
                File.WriteAllText("cursor_visual_status.txt", $"Failed to query IDXGISurface1: 0x{hrSurf:X}");
                return;
            }

            try
            {
                var getDC = Marshal.GetDelegateForFunctionPointer<GetDCDelegateInt>(
                    GetVtableFunc(dxgiSurfacePtr, 11));
                var releaseDC = Marshal.GetDelegateForFunctionPointer<ReleaseDCDelegate>(
                    GetVtableFunc(dxgiSurfacePtr, 12));

                int hrDc = getDC(dxgiSurfacePtr, 0, out IntPtr hdc);
                if (hrDc == 0 && hdc != IntPtr.Zero)
                {
                    try
                    {
                        using (var g = System.Drawing.Graphics.FromHdc(hdc))
                        {
                            var screenBounds = new System.Drawing.Rectangle(0, 0, w, h);
                            ScreenCaptureService.DrawCursorOnto(g, screenBounds, 1.0, 1.0);
                        }
                    }
                    finally
                    {
                        releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                    }
                }
                else
                {
                    File.WriteAllText("cursor_visual_status.txt", $"Failed to get DC: 0x{hrDc:X}");
                    return;
                }
            }
            finally
            {
                Marshal.Release(dxgiSurfacePtr);
            }

            // Copy back to CPU and save to PNG
            byte[]? bgra = dxgi.CopyFrameToCpu(dstTexture);
            if (bgra == null)
            {
                File.WriteAllText("cursor_visual_status.txt", "Failed to copy frame to CPU.");
                return;
            }

            using (var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                var rect = new System.Drawing.Rectangle(0, 0, w, h);
                var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat);
                Marshal.Copy(bgra, 0, bmpData.Scan0, w * h * 4);
                bmp.UnlockBits(bmpData);
                bmp.Save("cursor_preview.png", System.Drawing.Imaging.ImageFormat.Png);
            }

            File.WriteAllText("cursor_visual_status.txt", "Cursor visual composition test succeeded. Saved cursor_preview.png");
        }
        catch (Exception ex)
        {
            File.WriteAllText("cursor_visual_status.txt", $"Error in cursor visual composition: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            if (dstTexture != IntPtr.Zero) Marshal.Release(dstTexture);
            frame?.Release();
        }
    }

    [Fact]
    public void CheckIsSupportedDetails()
    {
        var logLines = new List<string>();
        try
        {
            // 1. GDI check on default adapter
            int hr = D3D11CreateDevice(
                IntPtr.Zero,
                1, // HARDWARE
                IntPtr.Zero,
                0x20, // BGRA
                IntPtr.Zero,
                0,
                7,
                out IntPtr device,
                out int featureLevel,
                out IntPtr context);

            logLines.Add($"D3D11CreateDevice (Default Device): HR=0x{hr:X}, FeatureLevel=0x{featureLevel:X}");
            if (hr == 0)
            {
                try
                {
                    // Attempt GDI on default device
                    var dstDesc = new D3D11_TEXTURE2D_DESC
                    {
                        Width = 64,
                        Height = 64,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                        SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                        Usage = 0,
                        BindFlags = 0x20 | 0x8,
                        CPUAccessFlags = 0,
                        MiscFlags = Direct3DConstants.D3D11_RESOURCE_MISC_GDI_COMPATIBLE
                    };

                    var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
                        GetVtableFunc(device, 5));

                    int hrTex = createTexture2D(device, ref dstDesc, IntPtr.Zero, out IntPtr dstTexture);
                    logLines.Add($"Create GDI Texture on Default Device: HR=0x{hrTex:X}");
                    if (hrTex == 0 && dstTexture != IntPtr.Zero)
                    {
                        try
                        {
                            Guid IID_IDXGISurface1 = new Guid("4ae63092-6327-4c1b-80ae-bfe12ea32b86");
                            int hrSurf = Marshal.QueryInterface(dstTexture, ref IID_IDXGISurface1, out IntPtr dxgiSurfacePtr);
                            logLines.Add($"Query IDXGISurface1 on Default Device: HR=0x{hrSurf:X}");
                            if (hrSurf == 0 && dxgiSurfacePtr != IntPtr.Zero)
                            {
                                try
                                {
                                    var getDC = Marshal.GetDelegateForFunctionPointer<GetDCDelegateInt>(
                                        GetVtableFunc(dxgiSurfacePtr, 11));
                                    int hrDc = getDC(dxgiSurfacePtr, 0, out IntPtr hdc);
                                    logLines.Add($"GetDC on Default Device: HR=0x{hrDc:X}");
                                    if (hrDc == 0 && hdc != IntPtr.Zero)
                                    {
                                        var releaseDC = Marshal.GetDelegateForFunctionPointer<ReleaseDCDelegate>(
                                            GetVtableFunc(dxgiSurfacePtr, 12));
                                        releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                                    }
                                }
                                finally
                                {
                                    Marshal.Release(dxgiSurfacePtr);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.Release(dstTexture);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logLines.Add($"GDI test on Default Device exception: {ex.Message}");
                }
                finally
                {
                    Marshal.Release(context);
                    Marshal.Release(device);
                }
            }

            // 2. Enumerate other adapters on the system
            try
            {
                Guid IID_IDXGIFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                int hrFactory = CreateDXGIFactory1(ref IID_IDXGIFactory1, out IntPtr factory);
                logLines.Add($"CreateDXGIFactory1: HR=0x{hrFactory:X}");
                if (hrFactory == 0 && factory != IntPtr.Zero)
                {
                    try
                    {
                        var enumAdapters = Marshal.GetDelegateForFunctionPointer<EnumAdaptersDelegate>(
                            GetVtableFunc(factory, 7)); // Index 7 is EnumAdapters
                        
                        for (uint i = 0; i < 5; i++)
                        {
                            int hrEnum = enumAdapters(factory, i, out IntPtr adapter);
                            logLines.Add($"  EnumAdapters({i}): HR=0x{hrEnum:X}, AdapterPtr=0x{adapter.ToString("X")}");
                            if (hrEnum != 0 || adapter == IntPtr.Zero) break;

                            try
                            {
                                // Get Adapter Desc
                                Guid IID_IDXGIAdapter = new Guid("2411e7e1-12ac-4ccf-bd14-9798e8534dc0");
                                int hrAd = Marshal.QueryInterface(adapter, ref IID_IDXGIAdapter, out IntPtr dxgiAdapter);
                                logLines.Add($"  Adapter {i}: QueryInterface HR=0x{hrAd:X}, dxgiAdapter=0x{dxgiAdapter.ToString("X")}");
                                if (hrAd == 0 && dxgiAdapter != IntPtr.Zero)
                                {
                                    try
                                    {
                                        var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(
                                            GetVtableFunc(dxgiAdapter, 8)); // Index 8 is GetDesc
                                        
                                        int hrDesc = getDesc(dxgiAdapter, out var desc);
                                        logLines.Add($"    GetDesc: HR=0x{hrDesc:X}");
                                        if (hrDesc == 0)
                                        {
                                            logLines.Add($"    Description: '{desc.Description}'");
                                            
                                            // Create Device on this adapter explicitly!
                                            int hrDev = D3D11CreateDevice(
                                                adapter,
                                                0, // D3D_DRIVER_TYPE_UNKNOWN (must be UNKNOWN if adapter is non-null)
                                                IntPtr.Zero,
                                                0x20, // BGRA
                                                IntPtr.Zero,
                                                0,
                                                7,
                                                out IntPtr dev,
                                                out _,
                                                out IntPtr ctx);

                                            logLines.Add($"    D3D11CreateDevice: HR=0x{hrDev:X}");
                                            if (hrDev == 0 && dev != IntPtr.Zero)
                                            {
                                                try
                                                {
                                                    // Try GDI texture
                                                    var dstDesc = new D3D11_TEXTURE2D_DESC
                                                    {
                                                        Width = 64,
                                                        Height = 64,
                                                        MipLevels = 1,
                                                        ArraySize = 1,
                                                        Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                                                        SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                                                        Usage = 0,
                                                        BindFlags = 0x20 | 0x8,
                                                        CPUAccessFlags = 0,
                                                        MiscFlags = Direct3DConstants.D3D11_RESOURCE_MISC_GDI_COMPATIBLE
                                                    };

                                                    var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
                                                        GetVtableFunc(dev, 5));

                                                    int hrTex = createTexture2D(dev, ref dstDesc, IntPtr.Zero, out IntPtr dstTexture);
                                                    logLines.Add($"      Create GDI Texture: HR=0x{hrTex:X}");
                                                    if (hrTex == 0 && dstTexture != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            Guid IID_IDXGISurface1 = new Guid("4ae63092-6327-4c1b-80ae-bfe12ea32b86");
                                                            int hrSurf = Marshal.QueryInterface(dstTexture, ref IID_IDXGISurface1, out IntPtr dxgiSurfacePtr);
                                                            logLines.Add($"      Query IDXGISurface1: HR=0x{hrSurf:X}");
                                                            if (hrSurf == 0 && dxgiSurfacePtr != IntPtr.Zero)
                                                            {
                                                                try
                                                                {
                                                                    var getDC = Marshal.GetDelegateForFunctionPointer<GetDCDelegateInt>(
                                                                        GetVtableFunc(dxgiSurfacePtr, 11));
                                                                    int hrDc = getDC(dxgiSurfacePtr, 0, out IntPtr hdc);
                                                                    logLines.Add($"      GetDC: HR=0x{hrDc:X}");
                                                                    if (hrDc == 0 && hdc != IntPtr.Zero)
                                                                    {
                                                                        var releaseDC = Marshal.GetDelegateForFunctionPointer<ReleaseDCDelegate>(
                                                                            GetVtableFunc(dxgiSurfacePtr, 12));
                                                                        releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                                                                    }
                                                                }
                                                                finally
                                                                {
                                                                    Marshal.Release(dxgiSurfacePtr);
                                                                }
                                                            }
                                                        }
                                                        finally
                                                        {
                                                            Marshal.Release(dstTexture);
                                                        }
                                                    }
                                                }
                                                finally
                                                {
                                                    Marshal.Release(ctx);
                                                    Marshal.Release(dev);
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        Marshal.Release(dxgiAdapter);
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.Release(adapter);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(factory);
                    }
                }
            }
            catch (Exception ex)
            {
                logLines.Add($"Adapter loop exception: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            logLines.Add($"Exception: {ex.Message}");
        }
        finally
        {
            File.WriteAllLines("dxgi_supported_details.txt", logLines);
        }
    }

    [DllImport("dxgi.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetAdapterDelegate(IntPtr thisPtr, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumOutputsDelegate(IntPtr thisPtr, uint outputIndex, out IntPtr ppOutput);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DuplicateOutputDelegate(IntPtr thisPtr, IntPtr pDevice, out IntPtr ppOutputDuplication);

    [DllImport("d3d11.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(IntPtr device, ref D3D11_TEXTURE2D_DESC desc, IntPtr initialData, out IntPtr texture2D);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(IntPtr context, IntPtr dst, IntPtr src);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDCDelegate(IntPtr surface, bool discard, out IntPtr hdc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDCDelegateInt(IntPtr surface, int discard, out IntPtr hdc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FlushDelegate(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdaptersDelegate(IntPtr thisPtr, uint adapterIndex, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescDelegate(IntPtr thisPtr, out DXGI_ADAPTER_DESC pDesc);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public LUID AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseDCDelegate(IntPtr surface, IntPtr dirtyRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_SAMPLE_DESC
    {
        public int Count;
        public int Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public int Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public int Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceDelegate(IntPtr thisPtr, IntPtr riid, out IntPtr ppvObject);

    private static IntPtr GetVtableFunc(IntPtr obj, int index)
    {
        IntPtr vtable = Marshal.ReadIntPtr(obj);
        return Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
    }

    [Fact]
    public void Profile_Bicubic_vs_Bilinear_CPU_Resizing()
    {
        // 1080p source image
        int srcW = 1920;
        int srcH = 1080;
        using var srcBmp = new System.Drawing.Bitmap(srcW, srcH, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        
        var targets = new[] { 720, 1080, 1440 }; // 720p, 1080p, 1440p
        var results = new System.Text.StringBuilder();
        results.AppendLine("CPU Resizing Profiling (Bilinear vs HighQualityBicubic) - 100 iterations:");

        foreach (var targetW in targets)
        {
            double ratio = (double)targetW / srcW;
            int targetH = (int)(srcH * ratio);

            using var destBmp = new System.Drawing.Bitmap(targetW, targetH, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

            // Bilinear
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                using (var g = System.Drawing.Graphics.FromImage(destBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(srcBmp, 0, 0, targetW, targetH);
                }
            }
            sw.Stop();
            double bilinearMs = sw.Elapsed.TotalMilliseconds / 100;

            // Bicubic
            sw.Reset();
            sw.Start();
            for (int i = 0; i < 100; i++)
            {
                using (var g = System.Drawing.Graphics.FromImage(destBmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(srcBmp, 0, 0, targetW, targetH);
                }
            }
            sw.Stop();
            double bicubicMs = sw.Elapsed.TotalMilliseconds / 100;

            results.AppendLine($"Target {targetW}x{targetH}:");
            results.AppendLine($"  Bilinear: {bilinearMs:F4} ms");
            results.AppendLine($"  HighQualityBicubic: {bicubicMs:F4} ms");
            results.AppendLine($"  Delta Overhead: {bicubicMs - bilinearMs:F4} ms");
        }

        System.IO.File.WriteAllText("cpu_resizing_profile.txt", results.ToString());
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
