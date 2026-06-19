namespace Pconnect.Agent.Services;

/// <summary>
/// Screen preview / streaming backends. Production today uses <see cref="JpegV1"/> only.
/// </summary>
internal static class ScreenStreamNegotiation
{
    public const string JpegV1 = "jpeg-v1";
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
}
