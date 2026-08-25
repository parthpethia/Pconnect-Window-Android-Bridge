using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using Vortice.MediaFoundation;

namespace Pconnect.Agent.Services;

internal sealed class H264EncoderService : IDisposable
{
    private static readonly bool AllowFFmpegFallback = 
        Environment.GetEnvironmentVariable("PCONNECT_ALLOW_FFMPEG_FALLBACK") == "true";

    private IMFTransform? _mft;
    private FFmpegVideoEncoder? _ffmpegEncoder;
    private string _encoderName = "x264 (software via FFmpeg)";
    private bool _isHardware;
    private bool _mftAllocates;
    private int _width;
    private int _height;
    private int _fps;
    private int _bitrateKbps;
    private int _frameCount;

    private IMFDXGIDeviceManager? _deviceManager;
    private IMFTransform? _videoProcessor;
    private IntPtr _nv12Texture = IntPtr.Zero;
    private IMFMediaBuffer? _nv12Buffer;
    private IMFSample? _nv12Sample;
    private bool _useGpuPath;

    private IntPtr _d3d11Device = IntPtr.Zero;
    private IntPtr _d3d11Context = IntPtr.Zero;
    private IntPtr _gdiTexture = IntPtr.Zero;
    private uint _gdiWidth = 0;
    private uint _gdiHeight = 0;

    public bool UseGpuPath => _useGpuPath;
    public string EncoderName => _encoderName;
    public bool IsHardware => _isHardware;
    internal static bool ForceInitializeSuccess { get; set; }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateDXGIDeviceManager(
        out uint pResetToken,
        out IntPtr ppDeviceManager);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateDXGISurfaceBuffer(
        in Guid riid,
        IntPtr punkSurface,
        uint uSubresourceIndex,
        bool fBottomUpWhenLinear,
        out IntPtr ppBuffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ResetDeviceDelegate(IntPtr thisPtr, IntPtr pUnkDevice, uint resetToken);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(IntPtr thisPtr, ref D3D11_TEXTURE2D_DESC pDesc, IntPtr pInitialData, out IntPtr ppTexture2D);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetTextureDescDelegate(IntPtr thisPtr, out D3D11_TEXTURE2D_DESC pDesc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetImmediateContextDelegate(IntPtr thisPtr, out IntPtr ppImmediateContext);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(IntPtr thisPtr, IntPtr pDstResource, IntPtr pSrcResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDCDelegate(IntPtr thisPtr, bool discard, out IntPtr hdc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseDCDelegate(IntPtr thisPtr, IntPtr dirtyRect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFTEnumEx(
        ref Guid guidCategory,
        uint flags,
        IntPtr pInputType,
        IntPtr pOutputType,
        out IntPtr pppMFTActivate,
        out uint pCount);

    private static readonly Guid MFT_CATEGORY_VIDEO_ENCODER = new("f79e8927-7abb-4509-b497-5884d8b849e1");
    private const uint MFT_ENUM_FLAG_HARDWARE = 0x00000004;
    private const uint MFT_ENUM_FLAG_ASYNCMFT = 0x00000008;

    private static readonly Guid MF_MT_MAJOR_TYPE = new("486717f7-dd39-4a2f-858d-01bc1a37e5e6");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42c8-47c4-95a4-ad935f7330b7");
    private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf73e990977");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b83b-55e999c60841");
    private static readonly Guid MF_MT_FRAME_RATE = new("c40a00f2-b93a-4d80-ae90-c6228416cbd3");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724d27-4b62-4ad4-9b17-d18c11494877");

    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_NV12 = new("3231564e-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFVideoFormat_ARGB32 = new("00000015-0000-0010-8000-00aa00389b71");

    [ComImport]
    [Guid("98666368-662f-4ea6-961b-f5e4e409134e")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICodecAPI
    {
        [PreserveSig] int IsSupported(in Guid api);
        [PreserveSig] int IsModifiable(in Guid api);
        [PreserveSig] int GetParameterRange(in Guid api, out object pMin, out object pMax, out object pSteppingDelta);
        [PreserveSig] int GetParameterValues(in Guid api, out IntPtr ppValues, out uint pCount);
        [PreserveSig] int GetValue(in Guid api, out object pValue);
        [PreserveSig] int SetValue(in Guid api, in object pValue);
    }

    public int CurrentBitrateKbps => _bitrateKbps;

    public bool UpdateBitrate(int targetBitrateKbps)
    {
        _bitrateKbps = targetBitrateKbps;
        if (ForceInitializeSuccess) return true;

        if (_mft != null)
        {
            try
            {
                IntPtr pUnknown = _mft.NativePointer;
                Guid iidCodecApi = new Guid("98666368-662f-4ea6-961b-f5e4e409134e");
                int hr = Marshal.QueryInterface(pUnknown, ref iidCodecApi, out IntPtr pCodecApi);
                if (hr == 0 && pCodecApi != IntPtr.Zero)
                {
                    try
                    {
                        var codecApi = (ICodecAPI)Marshal.GetObjectForIUnknown(pCodecApi);
                        Guid apiMeanBitRate = new Guid("1e8fe37f-6226-434b-af2d-2090d8a57111"); // CODECAPI_AVEncCommonMeanBitRate
                        uint bps = (uint)(targetBitrateKbps * 1000);
                        object val = bps;
                        int res = codecApi.SetValue(apiMeanBitRate, val);
                        if (res == 0)
                        {
                            Console.WriteLine($"[H264Encoder] Dynamic bitrate update succeeded: {targetBitrateKbps} Kbps via ICodecAPI.");
                            return true;
                        }
                    }
                    finally
                    {
                        Marshal.Release(pCodecApi);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[H264Encoder] Dynamic bitrate update exception: {ex.Message}");
            }
        }
        return false;
    }

    public H264EncoderService()
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MFT_REGISTER_TYPE_INFO
    {
        public Guid guidMajorType;
        public Guid guidSubtype;
    }

    private Guid? ProbeHardwareEncoders()
    {
        Console.WriteLine("[H264Encoder] Probing hardware encoders...");
        IntPtr pInput = IntPtr.Zero;
        IntPtr pOutput = IntPtr.Zero;
        try
        {
            var inputInfo = new MFT_REGISTER_TYPE_INFO
            {
                guidMajorType = MFMediaType_Video,
                guidSubtype = MFVideoFormat_NV12
            };
            var outputInfo = new MFT_REGISTER_TYPE_INFO
            {
                guidMajorType = MFMediaType_Video,
                guidSubtype = MFVideoFormat_H264
            };

            pInput = Marshal.AllocHGlobal(Marshal.SizeOf<MFT_REGISTER_TYPE_INFO>());
            pOutput = Marshal.AllocHGlobal(Marshal.SizeOf<MFT_REGISTER_TYPE_INFO>());

            Marshal.StructureToPtr(inputInfo, pInput, false);
            Marshal.StructureToPtr(outputInfo, pOutput, false);

            Guid guidCategory = MFT_CATEGORY_VIDEO_ENCODER;
            int hr = MFTEnumEx(
                ref guidCategory,
                MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_ASYNCMFT,
                pInput,
                pOutput,
                out IntPtr pppActivate,
                out uint count);

            Console.WriteLine($"[H264Encoder] MFTEnumEx with filters returned: HR=0x{hr:X}, Count={count}");

            if (hr == 0 && count > 0 && pppActivate != IntPtr.Zero)
            {
                IntPtr[] activates = new IntPtr[count];
                Marshal.Copy(pppActivate, activates, 0, (int)count);

                Guid? selectedClsid = null;
                for (int i = 0; i < count; i++)
                {
                    IntPtr activatePtr = activates[i];
                    if (activatePtr == IntPtr.Zero) continue;

                    var activate = new IMFActivate(activatePtr);
                    Guid clsidAttr = new Guid("c671878f-77ee-4af4-aa1e-0870ec4b96f5"); // MFT_TRANSFORM_CLSID_Attribute
                    
                    if (activate.GetGUID(clsidAttr, out Guid clsid) == SharpGen.Runtime.Result.Ok)
                    {
                        selectedClsid = clsid;
                        _encoderName = "Hardware MFT (" + clsid.ToString() + ")";
                        Console.WriteLine($"[H264Encoder] Found Hardware Encoder CLSID: {clsid}");
                        break;
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    if (activates[i] != IntPtr.Zero)
                    {
                        Marshal.Release(activates[i]);
                    }
                }
                Marshal.FreeCoTaskMem(pppActivate);
                return selectedClsid;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[H264Encoder] Error during hardware probe: {ex.Message}");
        }
        finally
        {
            if (pInput != IntPtr.Zero) Marshal.FreeHGlobal(pInput);
            if (pOutput != IntPtr.Zero) Marshal.FreeHGlobal(pOutput);
        }
        return null;
    }

    public void Initialize(int width, int height, int fps, int bitrateKbps, IntPtr d3d11Device = default)
    {
        if (ForceInitializeSuccess)
        {
            _width = width;
            _height = height;
            _fps = fps;
            _bitrateKbps = bitrateKbps;
            _frameCount = 0;
            _useGpuPath = (d3d11Device != IntPtr.Zero);
            _encoderName = "Mocked Encoder (Forced Success)";
            Console.WriteLine("[H264Encoder] Mocked initialization succeeded.");
            return;
        }

        _width = width;
        _height = height;
        _fps = fps;
        _bitrateKbps = bitrateKbps;
        _frameCount = 0;
        _useGpuPath = false;

        Exception? innerEx = null;
        try
        {
            MediaFactory.MFStartup(true);
            
            Guid? clsid = ProbeHardwareEncoders();
            if (clsid == null)
            {
                clsid = Guid.Parse("6ca50344-051a-4ded-9779-a43305165e35"); // CLSID_CMSH264EncoderMFT (software MFT)
                _isHardware = false;
                _encoderName = "Software MFT (Microsoft H.264 Encoder)";
                Console.WriteLine("[H264Encoder] Hardware encoder not found. Attempting software Media Foundation Encoder...");
            }
            else
            {
                _isHardware = true;
            }

            if (clsid != null)
            {
                Type? comType = Type.GetTypeFromCLSID(clsid.Value);
                if (comType == null) throw new Exception("Failed to get type from CLSID");
                
                object comObj = Activator.CreateInstance(comType)!;
                IntPtr punk = Marshal.GetIUnknownForObject(comObj);
                _mft = new IMFTransform(punk);
                
                SetupMft(_mft, width, height, fps, bitrateKbps);

                _d3d11Device = d3d11Device;

                // Initialize DXGI Device Manager and Video Processor MFT for GPU-resident conversion
                if (d3d11Device != IntPtr.Zero)
                {
                    try
                    {
                        var getImmediateContext = Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(
                            GetVtableFunc(d3d11Device, 40));
                        getImmediateContext(d3d11Device, out _d3d11Context);

                        int hr = MFCreateDXGIDeviceManager(out uint resetToken, out IntPtr ppDeviceManager);
                        if (hr == 0 && ppDeviceManager != IntPtr.Zero)
                        {
                            _deviceManager = new IMFDXGIDeviceManager(ppDeviceManager);

                            var resetDevice = Marshal.GetDelegateForFunctionPointer<ResetDeviceDelegate>(
                                GetVtableFunc(ppDeviceManager, 6)); // ResetDevice is index 6
                            hr = resetDevice(ppDeviceManager, d3d11Device, resetToken);
                            if (hr == 0)
                            {
                                Guid CLSID_VideoProcessorMFT = new Guid("1661d368-80f0-466d-a7a2-9442a8a8163f");
                                Type? vpType = Type.GetTypeFromCLSID(CLSID_VideoProcessorMFT);
                                if (vpType != null)
                                {
                                    object vpObj = Activator.CreateInstance(vpType)!;
                                    _videoProcessor = new IMFTransform(Marshal.GetIUnknownForObject(vpObj));

                                    SetupVideoProcessor(_videoProcessor, width, height, fps);

                                    // Set D3D11 Device Manager on both MFTs
                                    const int MFT_MESSAGE_SET_D3D_MANAGER = 1;
                                    _mft.ProcessMessage((TMessageType)MFT_MESSAGE_SET_D3D_MANAGER, (nuint)ppDeviceManager);
                                    _videoProcessor.ProcessMessage((TMessageType)MFT_MESSAGE_SET_D3D_MANAGER, (nuint)ppDeviceManager);

                                    // Allocate NV12 texture on GPU
                                    _nv12Texture = CreateNv12Texture(d3d11Device, width, height);

                                    // Wrap NV12 texture in DXGI surface media buffer
                                    Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
                                    hr = MFCreateDXGISurfaceBuffer(IID_ID3D11Texture2D, _nv12Texture, 0, false, out IntPtr pBufferPtr);
                                    if (hr == 0 && pBufferPtr != IntPtr.Zero)
                                    {
                                        _nv12Buffer = new IMFMediaBuffer(pBufferPtr);
                                        _nv12Sample = MediaFactory.MFCreateSample();
                                        _nv12Sample.AddBuffer(_nv12Buffer);
                                        _useGpuPath = true;
                                        Console.WriteLine("[H264Encoder] GPU-resident pipeline initialized successfully.");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[H264Encoder] Failed to initialize GPU-resident pipeline: {ex.Message}. Falling back to CPU copy.");
                        CleanupGpuResources();
                    }
                }
                
                OutputStreamInfo streamInfo = _mft.GetOutputStreamInfo(0);
                _mftAllocates = (streamInfo.Flags & 0x00000100) != 0; // MFT_OUTPUT_STREAM_PROVIDES_SAMPLES
                
                _mft.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
                _mft.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);

                if (_videoProcessor != null)
                {
                    _videoProcessor.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
                    _videoProcessor.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
                }
                
                Console.WriteLine($"[H264Encoder] Successfully initialized Media Foundation H.264 Encoder. Selected encoder: {_encoderName} (Hardware: {_isHardware}, GPU-resident: {_useGpuPath})");
                return;
            }
        }
        catch (Exception ex)
        {
            innerEx = ex;
            Console.WriteLine($"[H264Encoder] Media Foundation initialization failed: {ex.Message}.");
            _mft?.Dispose();
            _mft = null;
            CleanupGpuResources();
        }

        if (AllowFFmpegFallback)
        {
            try
            {
                _ffmpegEncoder = new FFmpegVideoEncoder();
                _encoderName = "x264 (software via FFmpeg)";
                Console.WriteLine("[H264Encoder] Software fallback (x264 via FFmpeg) initialized successfully.");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[H264Encoder] Critical: FFmpeg initialization failed: {ex.Message}");
                throw;
            }
        }
        else
        {
            throw new Exception("Media Foundation encoding initialization failed and FFmpeg fallback is disabled.", innerEx);
        }
    }

    private void SetupVideoProcessor(IMFTransform vp, int width, int height, int fps)
    {
        ulong frameSize = ((ulong)width << 32) | (uint)height;
        ulong frameRate = ((ulong)fps << 32) | 1;

        IMFMediaType inputType = MediaFactory.MFCreateMediaType();
        inputType.Set(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        inputType.Set(MF_MT_SUBTYPE, MFVideoFormat_ARGB32);
        inputType.Set(MF_MT_FRAME_SIZE, frameSize);
        inputType.Set(MF_MT_FRAME_RATE, frameRate);
        inputType.Set(MF_MT_INTERLACE_MODE, (uint)2); // Progressive
        vp.SetInputType(0, inputType, 0);

        IMFMediaType outputType = MediaFactory.MFCreateMediaType();
        outputType.Set(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        outputType.Set(MF_MT_SUBTYPE, MFVideoFormat_NV12);
        outputType.Set(MF_MT_FRAME_SIZE, frameSize);
        outputType.Set(MF_MT_FRAME_RATE, frameRate);
        outputType.Set(MF_MT_INTERLACE_MODE, (uint)2); // Progressive
        vp.SetOutputType(0, outputType, 0);
    }

    private IntPtr CreateNv12Texture(IntPtr device, int width, int height)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = 103, // DXGI_FORMAT_NV12
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = 0, // D3D11_USAGE_DEFAULT
            BindFlags = 0x20 | 0x8, // D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE
            CPUAccessFlags = 0,
            MiscFlags = 0
        };

        var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
            GetVtableFunc(device, 5)); // ID3D11Device::CreateTexture2D is index 5
        
        int hr = createTexture2D(device, ref desc, IntPtr.Zero, out IntPtr texture);
        if (hr != 0) throw new Exception($"Failed to create NV12 texture: 0x{hr:X}");
        return texture;
    }

    private IntPtr CreateGdiCompatibleTexture(IntPtr device, int width, int height)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = 0, // D3D11_USAGE_DEFAULT
            BindFlags = 0x20 | 0x8, // D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE
            CPUAccessFlags = 0,
            MiscFlags = Direct3DConstants.D3D11_RESOURCE_MISC_GDI_COMPATIBLE
        };

        var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
            GetVtableFunc(device, 5)); // ID3D11Device::CreateTexture2D is index 5
        
        int hr = createTexture2D(device, ref desc, IntPtr.Zero, out IntPtr texture);
        if (hr != 0) throw new Exception($"Failed to create GDI compatible texture: 0x{hr:X}");
        return texture;
    }

    private void CleanupGpuResources()
    {
        _useGpuPath = false;
        
        _nv12Sample?.Dispose();
        _nv12Sample = null;
        
        _nv12Buffer?.Dispose();
        _nv12Buffer = null;

        if (_nv12Texture != IntPtr.Zero)
        {
            Marshal.Release(_nv12Texture);
            _nv12Texture = IntPtr.Zero;
        }

        if (_gdiTexture != IntPtr.Zero)
        {
            Marshal.Release(_gdiTexture);
            _gdiTexture = IntPtr.Zero;
        }
        _gdiWidth = 0;
        _gdiHeight = 0;

        _videoProcessor?.Dispose();
        _videoProcessor = null;

        _deviceManager?.Dispose();
        _deviceManager = null;

        if (_d3d11Context != IntPtr.Zero)
        {
            Marshal.Release(_d3d11Context);
            _d3d11Context = IntPtr.Zero;
        }
        _d3d11Device = IntPtr.Zero;
    }

    private static IntPtr GetVtableFunc(IntPtr obj, int index)
    {
        IntPtr vtable = Marshal.ReadIntPtr(obj);
        return Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
    }

    private bool _pendingKeyframe;

    public void RequestKeyframe()
    {
        _pendingKeyframe = true;
    }

    private void SetupMft(IMFTransform mft, int width, int height, int fps, int bitrateKbps)
    {
        IMFMediaType outputType = MediaFactory.MFCreateMediaType();
        Console.WriteLine($"[H264Encoder] SetupMft: mft.NativePointer=0x{mft.NativePointer.ToString("X")}, outputType.NativePointer=0x{outputType.NativePointer.ToString("X")}");
        outputType.Set(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        outputType.Set(MF_MT_SUBTYPE, MFVideoFormat_H264);
        outputType.Set(MF_MT_AVG_BITRATE, (uint)(bitrateKbps * 1000));
        
        ulong frameSize = ((ulong)width << 32) | (uint)height;
        outputType.Set(MF_MT_FRAME_SIZE, frameSize);
        
        ulong frameRate = ((ulong)fps << 32) | 1;
        outputType.Set(MF_MT_FRAME_RATE, frameRate);
        outputType.Set(MF_MT_INTERLACE_MODE, (uint)2); // Progressive
        
        mft.SetOutputType(0, outputType, 0);

        IMFMediaType inputType = MediaFactory.MFCreateMediaType();
        inputType.Set(MF_MT_MAJOR_TYPE, MFMediaType_Video);
        inputType.Set(MF_MT_SUBTYPE, MFVideoFormat_NV12);
        inputType.Set(MF_MT_FRAME_SIZE, frameSize);
        inputType.Set(MF_MT_FRAME_RATE, frameRate);
        inputType.Set(MF_MT_INTERLACE_MODE, (uint)2); // Progressive

        mft.SetInputType(0, inputType, 0);

        // Configure long GOP (90 frames ~ 3 seconds at 30 FPS)
        try
        {
            IntPtr pUnknown = mft.NativePointer;
            Guid iidCodecApi = new Guid("98666368-662f-4ea6-961b-f5e4e409134e");
            int hr = Marshal.QueryInterface(pUnknown, ref iidCodecApi, out IntPtr pCodecApi);
            if (hr == 0 && pCodecApi != IntPtr.Zero)
            {
                try
                {
                    var codecApi = (ICodecAPI)Marshal.GetObjectForIUnknown(pCodecApi);
                    Guid apiGopSize = new Guid("553b08e0-081e-469a-9e1d-c8ef4c148f22"); // CODECAPI_AVEncVideoGOPSize
                    object val = (uint)90;
                    codecApi.SetValue(apiGopSize, val);
                }
                finally
                {
                    Marshal.Release(pCodecApi);
                }
            }
        }
        catch { /* best-effort GOP size hint */ }
    }

    public ReadOnlyMemory<byte> Encode(byte[] bgraPixels, int width, int height, bool forceKeyframe)
    {
        if (ForceInitializeSuccess)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (_mft == null)
        {
            if (_ffmpegEncoder == null)
                throw new InvalidOperationException("Encoder is not initialized.");

            byte[]? encoded = _ffmpegEncoder.EncodeVideo(width, height, bgraPixels, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.H264);
            return encoded == null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(encoded);
        }

        try
        {
            _frameCount++;

            bool shouldKeyframe = forceKeyframe || _pendingKeyframe || _frameCount == 1 || (_frameCount % 90 == 0);
            if (shouldKeyframe)
            {
                _pendingKeyframe = false;
                _mft.ProcessMessage(TMessageType.MessageCommandFlush, UIntPtr.Zero);
            }

            int frameSize = width * height;
            int requiredSize = frameSize + (frameSize / 2);
            byte[] nv12 = System.Buffers.ArrayPool<byte>.Shared.Rent(requiredSize);
            try
            {
                ConvertBgraToNv12(bgraPixels, width, height, nv12);

                IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(requiredSize);
                buffer.Lock(out IntPtr pbBuffer, out _, out _);
                Marshal.Copy(nv12, 0, pbBuffer, requiredSize);
                buffer.CurrentLength = requiredSize;
                buffer.Unlock();

                IMFSample sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(buffer);
                
                long frameDuration = 10000000 / _fps; // 100ns units
                sample.SampleTime = (_frameCount - 1) * frameDuration;
                sample.SampleDuration = frameDuration;

                _mft.ProcessInput(0, sample, 0);

                sample.Dispose();
                buffer.Dispose();
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(nv12);
            }

            OutputStreamInfo streamInfo = _mft.GetOutputStreamInfo(0);
            bool mftAllocates = (streamInfo.Flags & 0x00000100) != 0; // MFT_OUTPUT_STREAM_PROVIDES_SAMPLES

            IMFSample? outputSample = null;
            IMFMediaBuffer? outputBuffer = null;

            if (!mftAllocates)
            {
                outputSample = MediaFactory.MFCreateSample();
                outputBuffer = MediaFactory.MFCreateMemoryBuffer(streamInfo.Size);
                outputSample.AddBuffer(outputBuffer);
            }

            OutputDataBuffer outputBufferStruct = new OutputDataBuffer
            {
                StreamID = 0,
                Sample = outputSample,
                Status = 0,
                Events = null
            };

            try
            {
                ProcessOutputStatus status;
                var res = _mft.ProcessOutput(ProcessOutputFlags.None, 1, ref outputBufferStruct, out status);
                
                if (res.Failure)
                {
                    if (res.Code == unchecked((int)0xC00D6D72)) // MF_E_TRANSFORM_NEED_MORE_INPUT
                    {
                        return ReadOnlyMemory<byte>.Empty;
                    }
                    throw new Exception($"ProcessOutput failed with HRESULT 0x{res.Code:X}");
                }

                IMFSample outSample = outputBufferStruct.Sample;
                if (outSample != null)
                {
                    IMFMediaBuffer contiguousBuffer = outSample.ConvertToContiguousBuffer();
                    contiguousBuffer.Lock(out IntPtr pbOutBuffer, out _, out int cbCurrentLength);
                    byte[] encodedData = new byte[cbCurrentLength];
                    Marshal.Copy(pbOutBuffer, encodedData, 0, cbCurrentLength);
                    contiguousBuffer.Unlock();
                    contiguousBuffer.Dispose();
                    
                    if (mftAllocates)
                    {
                        outSample.Dispose();
                    }

                    return new ReadOnlyMemory<byte>(encodedData);
                }
            }
            finally
            {
                if (!mftAllocates)
                {
                    outputSample?.Dispose();
                    outputBuffer?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[H264Encoder] MFT Encode error: {ex.Message}.");
            _mft?.Dispose();
            _mft = null;
            
            if (AllowFFmpegFallback)
            {
                try
                {
                    _ffmpegEncoder = new FFmpegVideoEncoder();
                    byte[]? encoded = _ffmpegEncoder.EncodeVideo(width, height, bgraPixels, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.H264);
                    return encoded == null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(encoded);
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"[H264Encoder] Fallback encode also failed: {fallbackEx.Message}");
                }
            }
            throw;
        }

        return ReadOnlyMemory<byte>.Empty;
    }

    public bool DisableGpuCursorComposition { get; set; } = false;

    public ReadOnlyMemory<byte> EncodeGpuTexture(IntPtr gpuTexture, int width, int height, bool forceKeyframe)
    {
        if (ForceInitializeSuccess)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (!_useGpuPath || _videoProcessor == null || _mft == null || _nv12Sample == null)
        {
            throw new InvalidOperationException("GPU-resident path is not initialized.");
        }

        try
        {
            _frameCount++;

            bool shouldKeyframe = forceKeyframe || _pendingKeyframe || _frameCount == 1 || (_frameCount % 90 == 0);
            if (shouldKeyframe)
            {
                _pendingKeyframe = false;
                _mft.ProcessMessage(TMessageType.MessageCommandFlush, UIntPtr.Zero);
            }

            IntPtr sourceTexture = gpuTexture;

            if (!DisableGpuCursorComposition && _d3d11Device != IntPtr.Zero && _d3d11Context != IntPtr.Zero)
            {
                var getDesc = Marshal.GetDelegateForFunctionPointer<GetTextureDescDelegate>(
                    GetVtableFunc(gpuTexture, 10)); // Index 10 is GetDesc
                getDesc(gpuTexture, out var srcDesc);

                if (_gdiTexture == IntPtr.Zero || _gdiWidth != srcDesc.Width || _gdiHeight != srcDesc.Height)
                {
                    if (_gdiTexture != IntPtr.Zero)
                    {
                        Marshal.Release(_gdiTexture);
                        _gdiTexture = IntPtr.Zero;
                    }

                    _gdiTexture = CreateGdiCompatibleTexture(_d3d11Device, (int)srcDesc.Width, (int)srcDesc.Height);
                    _gdiWidth = srcDesc.Width;
                    _gdiHeight = srcDesc.Height;
                }

                var copyResource = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(
                    GetVtableFunc(_d3d11Context, 47)); // Index 47 is CopyResource
                copyResource(_d3d11Context, _gdiTexture, gpuTexture);

                Guid IID_IDXGISurface1 = new Guid("4ae63092-6327-4c1b-80ae-bfe12ea32b86");
                int hrSurf = Marshal.QueryInterface(_gdiTexture, ref IID_IDXGISurface1, out IntPtr dxgiSurfacePtr);
                if (hrSurf == 0 && dxgiSurfacePtr != IntPtr.Zero)
                {
                    try
                    {
                        var getDC = Marshal.GetDelegateForFunctionPointer<GetDCDelegate>(
                            GetVtableFunc(dxgiSurfacePtr, 11)); // Index 11 is GetDC
                        var releaseDC = Marshal.GetDelegateForFunctionPointer<ReleaseDCDelegate>(
                            GetVtableFunc(dxgiSurfacePtr, 12)); // Index 12 is ReleaseDC

                        int hrDc = getDC(dxgiSurfacePtr, false, out IntPtr hdc);
                        if (hrDc != 0 || hdc == IntPtr.Zero)
                        {
                            Console.WriteLine($"[H264Encoder] GetDC failed: 0x{hrDc:X}");
                        }
                        if (hrDc == 0 && hdc != IntPtr.Zero)
                        {
                            try
                            {
                                using (var g = System.Drawing.Graphics.FromHdc(hdc))
                                {
                                    var screenBounds = new System.Drawing.Rectangle(0, 0, (int)_gdiWidth, (int)_gdiHeight);
                                    double scaleX = (double)_gdiWidth / Math.Max(1, screenBounds.Width);
                                    double scaleY = (double)_gdiHeight / Math.Max(1, screenBounds.Height);
                                    ScreenCaptureService.DrawCursorOnto(g, screenBounds, scaleX, scaleY);
                                }
                            }
                            finally
                            {
                                releaseDC(dxgiSurfacePtr, IntPtr.Zero);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.Release(dxgiSurfacePtr);
                    }
                }

                sourceTexture = _gdiTexture;
            }

            Guid IID_ID3D11Texture2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
            int hr = MFCreateDXGISurfaceBuffer(IID_ID3D11Texture2D, sourceTexture, 0, false, out IntPtr pInputBufferPtr);
            if (hr != 0) throw new Exception($"MFCreateDXGISurfaceBuffer failed: 0x{hr:X}");

            using var inputBuffer = new IMFMediaBuffer(pInputBufferPtr);
            using var inputSample = MediaFactory.MFCreateSample();
            inputSample.AddBuffer(inputBuffer);

            long frameDuration = 10000000 / _fps; // 100ns units
            inputSample.SampleTime = (_frameCount - 1) * frameDuration;
            inputSample.SampleDuration = frameDuration;

            _videoProcessor.ProcessInput(0, inputSample, 0);

            OutputDataBuffer vpOutputBuffer = new OutputDataBuffer
            {
                StreamID = 0,
                Sample = _nv12Sample,
                Status = 0,
                Events = null
            };

            ProcessOutputStatus vpStatus;
            var vpRes = _videoProcessor.ProcessOutput(ProcessOutputFlags.None, 1, ref vpOutputBuffer, out vpStatus);
            if (vpRes.Failure)
            {
                throw new Exception($"VideoProcessor.ProcessOutput failed with HRESULT 0x{vpRes.Code:X}");
            }

            _nv12Sample.SampleTime = (_frameCount - 1) * frameDuration;
            _nv12Sample.SampleDuration = frameDuration;

            _mft.ProcessInput(0, _nv12Sample, 0);

            OutputStreamInfo streamInfo = _mft.GetOutputStreamInfo(0);
            bool mftAllocates = (streamInfo.Flags & 0x00000100) != 0;

            IMFSample? outputSample = null;
            IMFMediaBuffer? outputBuffer = null;

            if (!mftAllocates)
            {
                outputSample = MediaFactory.MFCreateSample();
                outputBuffer = MediaFactory.MFCreateMemoryBuffer(streamInfo.Size);
                outputSample.AddBuffer(outputBuffer);
            }

            OutputDataBuffer outputBufferStruct = new OutputDataBuffer
            {
                StreamID = 0,
                Sample = outputSample,
                Status = 0,
                Events = null
            };

            try
            {
                ProcessOutputStatus status;
                var res = _mft.ProcessOutput(ProcessOutputFlags.None, 1, ref outputBufferStruct, out status);

                if (res.Failure)
                {
                    if (res.Code == unchecked((int)0xC00D6D72)) // MF_E_TRANSFORM_NEED_MORE_INPUT
                    {
                        return ReadOnlyMemory<byte>.Empty;
                    }
                    throw new Exception($"ProcessOutput failed with HRESULT 0x{res.Code:X}");
                }

                IMFSample outSample = outputBufferStruct.Sample;
                if (outSample != null)
                {
                    IMFMediaBuffer contiguousBuffer = outSample.ConvertToContiguousBuffer();
                    contiguousBuffer.Lock(out IntPtr pbOutBuffer, out _, out int cbCurrentLength);
                    byte[] encodedData = new byte[cbCurrentLength];
                    Marshal.Copy(pbOutBuffer, encodedData, 0, cbCurrentLength);
                    contiguousBuffer.Unlock();
                    contiguousBuffer.Dispose();

                    if (mftAllocates)
                    {
                        outSample.Dispose();
                    }

                    return new ReadOnlyMemory<byte>(encodedData);
                }
            }
            finally
            {
                if (!mftAllocates)
                {
                    outputSample?.Dispose();
                    outputBuffer?.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[H264Encoder] MFT GPU Encode error: {ex.Message}.");
            _useGpuPath = false;
            CleanupGpuResources();
            throw;
        }

        return ReadOnlyMemory<byte>.Empty;
    }

    private static void ConvertBgraToNv12(byte[] bgra, int width, int height, byte[] nv12)
    {
        int frameSize = width * height;

        for (int i = 0; i < frameSize; i++)
        {
            int bgraIdx = i * 4;
            byte b = bgra[bgraIdx];
            byte g = bgra[bgraIdx + 1];
            byte r = bgra[bgraIdx + 2];

            int y = (int)(0.299 * r + 0.587 * g + 0.114 * b);
            nv12[i] = (byte)Math.Clamp(y, 0, 255);
        }

        int uvIdx = frameSize;
        for (int y = 0; y < height; y += 2)
        {
            for (int x = 0; x < width; x += 2)
            {
                int idx0 = (y * width + x) * 4;
                int idx1 = (y * width + (x + 1)) * 4;
                int idx2 = ((y + 1) * width + x) * 4;
                int idx3 = ((y + 1) * width + (x + 1)) * 4;

                int b = (bgra[idx0] + bgra[idx1] + bgra[idx2] + bgra[idx3]) / 4;
                int g = (bgra[idx0 + 1] + bgra[idx1 + 1] + bgra[idx2 + 1] + bgra[idx3 + 1]) / 4;
                int r = (bgra[idx0 + 2] + bgra[idx1 + 2] + bgra[idx2 + 2] + bgra[idx3 + 2]) / 4;

                int u = (int)(-0.169 * r - 0.331 * g + 0.500 * b + 128);
                int v = (int)(0.500 * r - 0.419 * g - 0.081 * b + 128);

                nv12[uvIdx++] = (byte)Math.Clamp(u, 0, 255);
                nv12[uvIdx++] = (byte)Math.Clamp(v, 0, 255);
            }
        }
    }

    public void Dispose()
    {
        CleanupGpuResources();
        if (_mft != null)
        {
            _mft.Dispose();
            _mft = null;
            try { MediaFactory.MFShutdown(); } catch { }
        }
        _ffmpegEncoder = null;
    }
}
