using System;
using System.Runtime.InteropServices;
using Pconnect.Agent.Resilience;

namespace Pconnect.Agent.Services;

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_RATIONAL
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_MODE_DESC
{
    public uint Width;
    public uint Height;
    public DXGI_RATIONAL RefreshRate;
    public int Format;
    public int ScanlineOrdering;
    public int Scaling;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_OUTDUPL_DESC
{
    public DXGI_MODE_DESC ModeDesc;
    public int Rotation;
    public bool DesktopImageInSystemMemory;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_OUTDUPL_POINTER_POSITION
{
    public POINT Position;
    public bool Visible;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_OUTDUPL_FRAME_INFO
{
    public long LastPresentTime;
    public long LastMouseUpdateTime;
    public uint AccumulatedFrames;
    public bool RectsCoalesced;
    public bool ProtectedContentMaskedOut;
    public DXGI_OUTDUPL_POINTER_POSITION PointerPosition;
    public uint TotalMetadataBufferSize;
    public uint PointerShapeBufferSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_TEXTURE2D_DESC
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

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_SAMPLE_DESC
{
    public uint Count;
    public uint Quality;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11_MAPPED_SUBRESOURCE
{
    public IntPtr pData;
    public uint RowPitch;
    public uint DepthPitch;
}

internal class DxgiFrame
{
    public IntPtr Texture { get; }
    public DXGI_OUTDUPL_FRAME_INFO FrameInfo { get; }
    public RECT[] DirtyRects { get; }
    private readonly Action _releaseAction;

    public DxgiFrame(IntPtr texture, DXGI_OUTDUPL_FRAME_INFO frameInfo, RECT[] dirtyRects, Action releaseAction)
    {
        Texture = texture;
        FrameInfo = frameInfo;
        DirtyRects = dirtyRects;
        _releaseAction = releaseAction;
    }

    public void Release()
    {
        _releaseAction();
    }
}

internal sealed class ScreenCaptureDxgi : IDisposable
{
    private const int D3D11_SDK_VERSION = 7;
    private const int D3D_DRIVER_TYPE_HARDWARE = 1;
    private const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    private const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    private const int D3D11_USAGE_STAGING = 3;
    private const uint D3D11_CPU_ACCESS_READ = 0x20000;

    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_IDXGIAdapter = new("2411e7e1-12ac-4ccf-bd14-9798e8534d00");
    private static readonly Guid IID_IDXGIOutput = new("ae02cee6-ee35-4cf0-bf97-c1912f864b99");
    private static readonly Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    private static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    private const int IDXGIDevice_GetAdapter_Index = 7;
    private const int IDXGIAdapter_EnumOutputs_Index = 7;
    private const int IDXGIOutput1_DuplicateOutput_Index = 22;
    
    private const int IDXGIOutputDuplication_GetDesc_Index = 7;
    private const int IDXGIOutputDuplication_AcquireNextFrame_Index = 8;
    private const int IDXGIOutputDuplication_GetFrameDirtyRects_Index = 9;
    private const int IDXGIOutputDuplication_ReleaseFrame_Index = 14;

    private const int ID3D11Device_CreateTexture2D_Index = 5;
    private const int ID3D11DeviceContext_CopyResource_Index = 47;
    private const int ID3D11DeviceContext_Map_Index = 14;
    private const int ID3D11DeviceContext_Unmap_Index = 15;

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

    [DllImport("dxgi.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    private IntPtr _device = IntPtr.Zero;
    private IntPtr _context = IntPtr.Zero;
    private IntPtr _duplication = IntPtr.Zero;
    private IntPtr _stagingTexture = IntPtr.Zero;
    
    private uint _width = 0;
    private uint _height = 0;
    private readonly object _lock = new();

    private readonly TimeWindowedCircuitBreaker _circuitBreaker = new(thresholdCount: 5, timeWindow: TimeSpan.FromSeconds(10), halfOpenTimeout: TimeSpan.FromSeconds(10));

    private uint _displayIndex = 0;

    internal static bool ForceInitializeSuccess { get; set; }

    public ScreenCaptureDxgi(uint displayIndex = 0)
    {
        _displayIndex = displayIndex;
        if (ForceInitializeSuccess)
        {
            _width = 1920;
            _height = 1080;
            return;
        }
        Initialize(_displayIndex);
    }

    internal static bool? ForceSupported { get; set; }

    public static bool IsSupported()
    {
        if (ForceSupported.HasValue) return ForceSupported.Value;

        try
        {
            int hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero,
                0,
                D3D11_SDK_VERSION,
                out IntPtr device,
                out _,
                out IntPtr context);

            if (hr != 0) return false;

            try
            {
                Guid iidDevice = IID_IDXGIDevice;
                int res = Marshal.QueryInterface(device, ref iidDevice, out IntPtr dxgiDevice);
                if (res != 0) return false;

                try
                {
                    var getAdapter = Marshal.GetDelegateForFunctionPointer<GetAdapterDelegate>(
                        GetVtableFunc(dxgiDevice, IDXGIDevice_GetAdapter_Index));
                    
                    res = getAdapter(dxgiDevice, out IntPtr adapter);
                    if (res != 0) return false;

                    try
                    {
                        var enumOutputs = Marshal.GetDelegateForFunctionPointer<EnumOutputsDelegate>(
                            GetVtableFunc(adapter, IDXGIAdapter_EnumOutputs_Index));
                        
                        res = enumOutputs(adapter, 0, out IntPtr output);
                        if (res != 0) return false;

                        try
                        {
                            Guid iidOutput1 = IID_IDXGIOutput1;
                            res = Marshal.QueryInterface(output, ref iidOutput1, out IntPtr output1);
                            if (res != 0) return false;

                            try
                            {
                                var duplicateOutput = Marshal.GetDelegateForFunctionPointer<DuplicateOutputDelegate>(
                                    GetVtableFunc(output1, IDXGIOutput1_DuplicateOutput_Index));
                                
                                res = duplicateOutput(output1, device, out IntPtr duplication);
                                if (res == 0 && duplication != IntPtr.Zero)
                                {
                                    Marshal.Release(duplication);
                                    return true;
                                }
                            }
                            finally
                            {
                                Marshal.Release(output1);
                            }
                        }
                        finally
                        {
                            Marshal.Release(output);
                        }
                    }
                    finally
                    {
                        Marshal.Release(adapter);
                    }
                }
                finally
                {
                    Marshal.Release(dxgiDevice);
                }
            }
            finally
            {
                Marshal.Release(context);
                Marshal.Release(device);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal void Initialize(uint? displayIndex = null)
    {
        if (displayIndex.HasValue) _displayIndex = displayIndex.Value;

        lock (_lock)
        {
            ReleaseResources();

            int hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3D_DRIVER_TYPE_HARDWARE,
                IntPtr.Zero,
                D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                IntPtr.Zero,
                0,
                D3D11_SDK_VERSION,
                out _device,
                out _,
                out _context);

            if (hr != 0) throw new Exception($"Failed to create D3D11 device. HR = {hr}");

            Guid iidDevice = IID_IDXGIDevice;
            int res = Marshal.QueryInterface(_device, ref iidDevice, out IntPtr dxgiDevice);
            if (res != 0) throw new Exception("Failed to query IDXGIDevice.");

            try
            {
                var getAdapter = Marshal.GetDelegateForFunctionPointer<GetAdapterDelegate>(
                    GetVtableFunc(dxgiDevice, IDXGIDevice_GetAdapter_Index));
                
                res = getAdapter(dxgiDevice, out IntPtr adapter);
                if (res != 0) throw new Exception("Failed to get DXGI adapter.");

                try
                {
                    var enumOutputs = Marshal.GetDelegateForFunctionPointer<EnumOutputsDelegate>(
                        GetVtableFunc(adapter, IDXGIAdapter_EnumOutputs_Index));
                    
                    res = enumOutputs(adapter, _displayIndex, out IntPtr output);
                    if (res != 0 && _displayIndex != 0)
                    {
                        _displayIndex = 0;
                        res = enumOutputs(adapter, 0, out output);
                    }
                    if (res != 0) throw new Exception($"Failed to enumerate DXGI output {_displayIndex}.");

                    try
                    {
                        Guid iidOutput1 = IID_IDXGIOutput1;
                        res = Marshal.QueryInterface(output, ref iidOutput1, out IntPtr output1);
                        if (res != 0) throw new Exception("Failed to query IDXGIOutput1.");

                        try
                        {
                            var duplicateOutput = Marshal.GetDelegateForFunctionPointer<DuplicateOutputDelegate>(
                                GetVtableFunc(output1, IDXGIOutput1_DuplicateOutput_Index));
                            
                            res = duplicateOutput(output1, _device, out _duplication);
                            if (res != 0) throw new Exception($"Failed to duplicate output. HR = {res}");
                        }
                        finally
                        {
                            Marshal.Release(output1);
                        }
                    }
                    finally
                    {
                        Marshal.Release(output);
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }

            var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(
                GetVtableFunc(_duplication, IDXGIOutputDuplication_GetDesc_Index));
            
            getDesc(_duplication, out var desc);
            _width = desc.ModeDesc.Width;
            _height = desc.ModeDesc.Height;

            CreateStagingTexture();
            _circuitBreaker.RecordSuccess();
        }
    }

    private void CreateStagingTexture()
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = _width,
            Height = _height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
            Usage = D3D11_USAGE_STAGING,
            BindFlags = 0,
            CPUAccessFlags = D3D11_CPU_ACCESS_READ,
            MiscFlags = 0
        };

        var createTexture2D = Marshal.GetDelegateForFunctionPointer<CreateTexture2DDelegate>(
            GetVtableFunc(_device, ID3D11Device_CreateTexture2D_Index));

        int hr = createTexture2D(_device, ref desc, IntPtr.Zero, out _stagingTexture);
        if (hr != 0) throw new Exception($"Failed to create staging texture. HR = {hr}");
    }

    public DxgiFrame? AcquireNextFrame(int timeoutMs)
    {
        if (_circuitBreaker.IsOpen) return null;

        lock (_lock)
        {
            if (_duplication == IntPtr.Zero)
            {
                try { Initialize(); }
                catch
                {
                    _circuitBreaker.RecordFailure();
                    return null;
                }
            }

            var acquireNextFrame = Marshal.GetDelegateForFunctionPointer<AcquireNextFrameDelegate>(
                GetVtableFunc(_duplication, IDXGIOutputDuplication_AcquireNextFrame_Index));

            int hr = acquireNextFrame(_duplication, (uint)timeoutMs, out var frameInfo, out IntPtr resource);
            if (hr == unchecked((int)0x887A0027)) // DXGI_ERROR_WAIT_TIMEOUT
            {
                return null;
            }
            if (hr != 0) // e.g. DXGI_ERROR_ACCESS_LOST
            {
                _circuitBreaker.RecordFailure();
                try
                {
                    Initialize();
                }
                catch
                {
                    ReleaseResources();
                }
                return null;
            }

            _circuitBreaker.RecordSuccess();

            Guid iidTexture2d = IID_ID3D11Texture2D;
            int res = Marshal.QueryInterface(resource, ref iidTexture2d, out IntPtr texture);
            Marshal.Release(resource);

            if (res != 0)
            {
                var releaseFrame = Marshal.GetDelegateForFunctionPointer<ReleaseFrameDelegate>(
                    GetVtableFunc(_duplication, IDXGIOutputDuplication_ReleaseFrame_Index));
                releaseFrame(_duplication);
                return null;
            }

            RECT[] dirtyRects = Array.Empty<RECT>();
            if (frameInfo.TotalMetadataBufferSize > 0)
            {
                uint bufferSizeRequired = 0;
                var getFrameDirtyRects = Marshal.GetDelegateForFunctionPointer<GetFrameDirtyRectsDelegate>(
                    GetVtableFunc(_duplication, IDXGIOutputDuplication_GetFrameDirtyRects_Index));
                
                int hrDirty = getFrameDirtyRects(_duplication, 0, IntPtr.Zero, out bufferSizeRequired);
                if (bufferSizeRequired > 0)
                {
                    IntPtr buffer = Marshal.AllocHGlobal((int)bufferSizeRequired);
                    try
                    {
                        hrDirty = getFrameDirtyRects(_duplication, bufferSizeRequired, buffer, out _);
                        if (hrDirty == 0)
                        {
                            int rectSize = Marshal.SizeOf<RECT>();
                            int rectCount = (int)(bufferSizeRequired / rectSize);
                            dirtyRects = new RECT[rectCount];
                            for (int i = 0; i < rectCount; i++)
                            {
                                IntPtr ptr = IntPtr.Add(buffer, i * rectSize);
                                dirtyRects[i] = Marshal.PtrToStructure<RECT>(ptr);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }

            return new DxgiFrame(texture, frameInfo, dirtyRects, () =>
            {
                lock (_lock)
                {
                    Marshal.Release(texture);
                    if (_duplication != IntPtr.Zero)
                    {
                        var releaseFrame = Marshal.GetDelegateForFunctionPointer<ReleaseFrameDelegate>(
                            GetVtableFunc(_duplication, IDXGIOutputDuplication_ReleaseFrame_Index));
                        releaseFrame(_duplication);
                    }
                }
            });
        }
    }

    public Task<DxgiFrame?> AcquireNextFrameAsync(int timeoutMs)
    {
        return Task.Run(() => AcquireNextFrame(timeoutMs));
    }

    public byte[]? CopyFrameToCpu(IntPtr gpuTexture)
    {
        lock (_lock)
        {
            if (_device == IntPtr.Zero || _context == IntPtr.Zero || _stagingTexture == IntPtr.Zero)
            {
                return null;
            }

            var copyResource = Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(
                GetVtableFunc(_context, ID3D11DeviceContext_CopyResource_Index));
            
            copyResource(_context, _stagingTexture, gpuTexture);

            var map = Marshal.GetDelegateForFunctionPointer<MapDelegate>(
                GetVtableFunc(_context, ID3D11DeviceContext_Map_Index));

            int hr = map(_context, _stagingTexture, 0, 1, 0, out var mapped);
            if (hr != 0) return null;

            try
            {
                int pitch = (int)mapped.RowPitch;
                int byteCount = (int)(pitch * _height);
                byte[] raw = new byte[byteCount];
                Marshal.Copy(mapped.pData, raw, 0, byteCount);

                int expectedPitch = (int)(_width * 4);
                if (pitch == expectedPitch)
                {
                    return raw;
                }
                else
                {
                    byte[] contiguous = new byte[_width * _height * 4];
                    for (int y = 0; y < _height; y++)
                    {
                        Array.Copy(raw, y * pitch, contiguous, y * expectedPitch, expectedPitch);
                    }
                    return contiguous;
                }
            }
            finally
            {
                var unmap = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(
                    GetVtableFunc(_context, ID3D11DeviceContext_Unmap_Index));
                unmap(_context, _stagingTexture, 0);
            }
        }
    }

    public uint Width => _width;
    public uint Height => _height;
    public IntPtr Device => _device;
    public IntPtr Context => _context;

    private static IntPtr GetVtableFunc(IntPtr obj, int index)
    {
        IntPtr vtable = Marshal.ReadIntPtr(obj);
        return Marshal.ReadIntPtr(vtable, index * IntPtr.Size);
    }

    private void ReleaseResources()
    {
        if (_stagingTexture != IntPtr.Zero)
        {
            Marshal.Release(_stagingTexture);
            _stagingTexture = IntPtr.Zero;
        }
        if (_duplication != IntPtr.Zero)
        {
            Marshal.Release(_duplication);
            _duplication = IntPtr.Zero;
        }
        if (_context != IntPtr.Zero)
        {
            Marshal.Release(_context);
            _context = IntPtr.Zero;
        }
        if (_device != IntPtr.Zero)
        {
            Marshal.Release(_device);
            _device = IntPtr.Zero;
        }
        _width = 0;
        _height = 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            ReleaseResources();
        }
    }

    // Delegates for vtable calls
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetAdapterDelegate(IntPtr thisPtr, out IntPtr pAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumOutputsDelegate(IntPtr thisPtr, uint outputIndex, out IntPtr ppOutput);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DuplicateOutputDelegate(IntPtr thisPtr, IntPtr pDevice, out IntPtr ppDuplication);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDescDelegate(IntPtr thisPtr, out DXGI_OUTDUPL_DESC pDesc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AcquireNextFrameDelegate(IntPtr thisPtr, uint timeoutInMilliseconds, out DXGI_OUTDUPL_FRAME_INFO pFrameInfo, out IntPtr ppDesktopResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseFrameDelegate(IntPtr thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DDelegate(IntPtr thisPtr, ref D3D11_TEXTURE2D_DESC pDesc, IntPtr pInitialData, out IntPtr ppTexture2D);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopyResourceDelegate(IntPtr thisPtr, IntPtr pDstResource, IntPtr pSrcResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapDelegate(IntPtr thisPtr, IntPtr pResource, uint subresource, int mapType, uint mapFlags, out D3D11_MAPPED_SUBRESOURCE pMappedResource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnmapDelegate(IntPtr thisPtr, IntPtr pResource, uint subresource);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetFrameDirtyRectsDelegate(IntPtr thisPtr, uint dirtyRectsBufferSize, IntPtr pDirtyRectBuffer, out uint pDirtyRectsBufferSizeRequired);
}
