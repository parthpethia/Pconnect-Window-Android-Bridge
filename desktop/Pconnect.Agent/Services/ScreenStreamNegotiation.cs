namespace Pconnect.Agent.Services;

/// <summary>
/// Screen preview / streaming backends.
/// Priority: <see cref="WebRtcV1"/> → <see cref="JpegBinV1"/> → <see cref="JpegV1"/>.
/// </summary>
internal static class ScreenStreamNegotiation
{
    public const string JpegV1 = "jpeg-v1";
    public const string JpegBinV1 = "jpeg-bin-v1";
    public const string WebRtcV1 = "webrtc-v1";

    public static IReadOnlyList<string> AgentSupportedModes(SafeStartupOptions safe)
    {
        if (safe.IsSafeMode || safe.DisableScreenCapture)
        {
            return Array.Empty<string>();
        }

        var modes = new List<string>();
        if (ScreenCaptureDxgi.IsSupported())
        {
            modes.Add(WebRtcV1);
        }
        modes.Add(JpegBinV1);
        modes.Add(JpegV1);

        return modes;
    }

    /// <summary>
    /// Picks the first client-preferred mode the server supports; otherwise first server mode.
    /// </summary>
    public static string? Negotiate(IReadOnlyList<string>? clientPreference, IReadOnlyList<string> serverSupported)
    {
        if (serverSupported.Count == 0)
        {
            return null;
        }

        if (clientPreference is { Count: > 0 })
        {
            foreach (var mode in clientPreference)
            {
                if (string.IsNullOrWhiteSpace(mode))
                {
                    continue;
                }

                if (serverSupported.Contains(mode, StringComparer.Ordinal))
                {
                    return mode;
                }
            }
        }

        return serverSupported[0];
    }

    /// <summary>
    /// Computes the target bitrate in Kbps for WebRTC streaming based on client-requested quality.
    /// Presets: 75 -> 3000 Kbps (Normal), 80 -> 5500 Kbps (High), 90 -> 9000 Kbps (Best).
    /// </summary>
    public static int GetWebRtcTargetBitrate(int clientQuality)
    {
        return clientQuality switch
        {
            <= 75 => 3000,
            <= 80 => 5500,
            _ => 9000
        };
    }
}

