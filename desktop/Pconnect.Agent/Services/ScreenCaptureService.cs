using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Pconnect.Agent.Services;

/// <summary>
/// Captures the primary screen at a configurable interval, resizes to a thumbnail,
/// and JPEG-compresses it for transmission over WebSocket.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ScreenCaptureService : IDisposable
{
    private System.Threading.Timer? _timer;
    private readonly Action<string, int, int>? _onFrame; // base64, width, height
    private readonly Action<byte[], int, int>? _onRawFrame; // raw JPEG bytes, width, height
    private readonly object _gate = new();
    private bool _running;
    private int _intervalMs = 2000;
    private int _targetWidth = 720;
    private long _jpegQuality = 65L;
    private int _captureBusy;

    private static readonly ImageCodecInfo? JpegCodec = GetJpegCodecInfo();

    private const int CursorShowing = 0x00000001;
    private const int DiNormal = 0x0003;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int CbSize;
        public int Flags;
        public IntPtr HCursor;
        public PointNative PtScreenPos;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(
        IntPtr hdc,
        int xLeft,
        int yTop,
        IntPtr hIcon,
        int cxWidth,
        int cyHeight,
        int istepIfAniCur,
        IntPtr hbrFlickerFreeDraw,
        int diFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool FIcon;
        public int XHotspot;
        public int YHotspot;
        public IntPtr HbmMask;
        public IntPtr HbmColor;
    }

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo piconinfo);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public ScreenCaptureService(Action<string, int, int>? onFrame)
    {
        _onFrame = onFrame;
    }

    /// <summary>
    /// Constructs a capture service that delivers raw JPEG bytes (no Base64 conversion).
    /// Used by jpeg-bin-v1 binary transport.
    /// </summary>
    public ScreenCaptureService(Action<byte[], int, int> onRawFrame)
    {
        _onRawFrame = onRawFrame;
    }

    public void Start(int intervalMs = 1000, int? targetWidth = null, long? jpegQuality = null)
    {
        lock (_gate)
        {
            if (_running) return;
            _running = true;
            _intervalMs = Math.Max(100, intervalMs);
            if (targetWidth is > 0 and <= 1920) _targetWidth = targetWidth.Value;
            if (jpegQuality is > 0 and <= 100) _jpegQuality = jpegQuality.Value;
            // First frame on thread pool so timer setup returns immediately.
            _timer = new System.Threading.Timer(CaptureCallback, null, _intervalMs, _intervalMs);
            ThreadPool.QueueUserWorkItem(_ => CaptureCallback(null));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _running = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void CaptureCallback(object? state)
    {
        lock (_gate)
        {
            if (!_running) return;
        }

        if (Interlocked.Exchange(ref _captureBusy, 1) == 1)
        {
            return;
        }

        try
        {
            if (_onRawFrame != null)
            {
                var (jpegBytes, width, height) = CaptureScreenRaw();
                if (jpegBytes != null)
                {
                    _onRawFrame.Invoke(jpegBytes, width, height);
                }
            }
            else
            {
                var (base64, width, height) = CaptureScreen();
                if (base64 != null)
                {
                    _onFrame?.Invoke(base64, width, height);
                }
            }
        }
        catch
        {
            // Fail silently — screen capture may not be available in some contexts
        }
        finally
        {
            Interlocked.Exchange(ref _captureBusy, 0);
        }
    }

    private (string? base64, int width, int height) CaptureScreen()
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
            if (bounds == null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0)
            {
                return (null, 0, 0);
            }

            var screenWidth = bounds.Value.Width;
            var screenHeight = bounds.Value.Height;

            using var fullBitmap = new Bitmap(screenWidth, screenHeight);
            using (var g = Graphics.FromImage(fullBitmap))
            {
                g.CopyFromScreen(bounds.Value.Location, Point.Empty, bounds.Value.Size);
                DrawCursorOnto(g, bounds.Value);
            }

            // Resize to target width with high quality
            var ratio = (double)_targetWidth / screenWidth;
            var targetHeight = (int)(screenHeight * ratio);

            using var thumbnail = new Bitmap(_targetWidth, targetHeight);
            using (var g = Graphics.FromImage(thumbnail))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(fullBitmap, 0, 0, _targetWidth, targetHeight);
            }

            // Compress to JPEG
            using var ms = new MemoryStream();
            if (JpegCodec != null)
            {
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, _jpegQuality);
                thumbnail.Save(ms, JpegCodec, encoderParams);
            }
            else
            {
                thumbnail.Save(ms, ImageFormat.Jpeg);
            }

            var base64 = Convert.ToBase64String(ms.ToArray());
            return (base64, _targetWidth, targetHeight);
        }
        catch
        {
            return (null, 0, 0);
        }
    }

    /// <summary>
    /// Captures the screen and returns raw JPEG bytes (no Base64 conversion).
    /// </summary>
    private (byte[]? jpegBytes, int width, int height) CaptureScreenRaw()
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
            if (bounds == null || bounds.Value.Width <= 0 || bounds.Value.Height <= 0)
            {
                return (null, 0, 0);
            }

            var screenWidth = bounds.Value.Width;
            var screenHeight = bounds.Value.Height;

            using var fullBitmap = new Bitmap(screenWidth, screenHeight);
            using (var g = Graphics.FromImage(fullBitmap))
            {
                g.CopyFromScreen(bounds.Value.Location, Point.Empty, bounds.Value.Size);
                DrawCursorOnto(g, bounds.Value);
            }

            var ratio = (double)_targetWidth / screenWidth;
            var targetHeight = (int)(screenHeight * ratio);

            using var thumbnail = new Bitmap(_targetWidth, targetHeight);
            using (var g = Graphics.FromImage(thumbnail))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.DrawImage(fullBitmap, 0, 0, _targetWidth, targetHeight);
            }

            using var ms = new MemoryStream();
            if (JpegCodec != null)
            {
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, _jpegQuality);
                thumbnail.Save(ms, JpegCodec, encoderParams);
            }
            else
            {
                thumbnail.Save(ms, ImageFormat.Jpeg);
            }

            return (ms.ToArray(), _targetWidth, targetHeight);
        }
        catch
        {
            return (null, 0, 0);
        }
    }

    private static ImageCodecInfo? GetJpegCodecInfo()
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid)
            {
                return codec;
            }
        }
        return null;
    }

    /// <summary>
    /// GDI screen capture does not include the pointer; composite it so remote preview is usable.
    /// </summary>
    internal static void DrawCursorOnto(Graphics g, Rectangle screenBounds, double scaleX = 1.0, double scaleY = 1.0)
    {
        var ci = new CursorInfo { CbSize = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref ci) || (ci.Flags & CursorShowing) == 0 || ci.HCursor == IntPtr.Zero)
        {
            return;
        }

        var x = (int)((ci.PtScreenPos.X - screenBounds.X) * scaleX);
        var y = (int)((ci.PtScreenPos.Y - screenBounds.Y) * scaleY);

        if (GetIconInfo(ci.HCursor, out var iconInfo))
        {
            x -= (int)(iconInfo.XHotspot * scaleX);
            y -= (int)(iconInfo.YHotspot * scaleY);
            if (iconInfo.HbmColor != IntPtr.Zero)
            {
                DeleteObject(iconInfo.HbmColor);
            }

            if (iconInfo.HbmMask != IntPtr.Zero)
            {
                DeleteObject(iconInfo.HbmMask);
            }
        }

        if (x < -64 || y < -64 || x > (screenBounds.Width * scaleX) + 64 || y > (screenBounds.Height * scaleY) + 64)
        {
            return;
        }

        var hdc = g.GetHdc();
        try
        {
            int cursorWidth = scaleX == 1.0 ? 0 : (int)(32 * scaleX);
            int cursorHeight = scaleY == 1.0 ? 0 : (int)(32 * scaleY);
            DrawIconEx(hdc, x, y, ci.HCursor, cursorWidth, cursorHeight, 0, IntPtr.Zero, DiNormal);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
