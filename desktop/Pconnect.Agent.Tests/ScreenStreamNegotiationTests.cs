using Pconnect.Agent.Services;
using Xunit;

namespace Pconnect.Agent.Tests;

public sealed class ScreenStreamNegotiationTests
{
    [Fact]
    public void Negotiate_prefers_client_order_when_supported()
    {
        var client = new[] { ScreenStreamNegotiation.WebRtcV1, ScreenStreamNegotiation.JpegV1 };
        var server = new[] { ScreenStreamNegotiation.JpegV1, ScreenStreamNegotiation.WebRtcV1 };

        Assert.Equal(ScreenStreamNegotiation.WebRtcV1, ScreenStreamNegotiation.Negotiate(client, server));
    }

    [Fact]
    public void Negotiate_falls_back_to_server_default_when_client_unspecified()
    {
        var server = new[] { ScreenStreamNegotiation.JpegV1 };

        Assert.Equal(ScreenStreamNegotiation.JpegV1, ScreenStreamNegotiation.Negotiate(null, server));
    }

    [Fact]
    public void Negotiate_returns_null_when_server_offers_nothing()
    {
        Assert.Null(ScreenStreamNegotiation.Negotiate(new[] { ScreenStreamNegotiation.JpegV1 }, Array.Empty<string>()));
    }

    [Fact]
    public void AgentSupportedModes_excludes_capture_in_safe_mode()
    {
        var safe = SafeStartupOptions.Create(new[] { "test" });

        Assert.Empty(ScreenStreamNegotiation.AgentSupportedModes(safe));
    }

    [Fact]
    public void WebRtcV1_is_first_when_dxgi_supported()
    {
        var safe = SafeStartupOptions.Normal;
        var modes = ScreenStreamNegotiation.AgentSupportedModes(safe);
        if (ScreenCaptureDxgi.IsSupported())
        {
            Assert.Contains(ScreenStreamNegotiation.WebRtcV1, modes);
            Assert.Equal(ScreenStreamNegotiation.WebRtcV1, modes[0]);
        }
        else
        {
            Assert.DoesNotContain(ScreenStreamNegotiation.WebRtcV1, modes);
        }
    }

    [Fact]
    public void Negotiate_webrtc_preferred_by_client_and_server()
    {
        var client = new[] { ScreenStreamNegotiation.WebRtcV1, ScreenStreamNegotiation.JpegV1 };
        var server = new[] { ScreenStreamNegotiation.WebRtcV1, ScreenStreamNegotiation.JpegV1 };
        Assert.Equal(ScreenStreamNegotiation.WebRtcV1, ScreenStreamNegotiation.Negotiate(client, server));
    }

    [Fact]
    public void Negotiate_falls_back_when_server_has_no_webrtc()
    {
        var client = new[] { ScreenStreamNegotiation.WebRtcV1, ScreenStreamNegotiation.JpegV1 };
        var server = new[] { ScreenStreamNegotiation.JpegV1 };
        Assert.Equal(ScreenStreamNegotiation.JpegV1, ScreenStreamNegotiation.Negotiate(client, server));
    }

    [Fact]
    public void JpegBinV1_appears_in_AgentSupportedModes_between_webrtc_and_jpeg()
    {
        var safe = SafeStartupOptions.Normal;
        var modes = ScreenStreamNegotiation.AgentSupportedModes(safe);
        Assert.Contains(ScreenStreamNegotiation.JpegBinV1, modes);
        Assert.Contains(ScreenStreamNegotiation.JpegV1, modes);

        var modesList = modes.ToList();
        var binIdx = modesList.IndexOf(ScreenStreamNegotiation.JpegBinV1);
        var jpegIdx = modesList.IndexOf(ScreenStreamNegotiation.JpegV1);
        Assert.True(binIdx < jpegIdx, "jpeg-bin-v1 should come before jpeg-v1");

        if (ScreenCaptureDxgi.IsSupported())
        {
            var rtcIdx = modesList.IndexOf(ScreenStreamNegotiation.WebRtcV1);
            Assert.True(rtcIdx < binIdx, "webrtc-v1 should come before jpeg-bin-v1");
        }
    }

    [Fact]
    public void Negotiate_jpegBinV1_preferred_over_jpegV1()
    {
        var client = new[] { ScreenStreamNegotiation.WebRtcV1, ScreenStreamNegotiation.JpegBinV1, ScreenStreamNegotiation.JpegV1 };
        var server = new[] { ScreenStreamNegotiation.JpegBinV1, ScreenStreamNegotiation.JpegV1 };
        Assert.Equal(ScreenStreamNegotiation.JpegBinV1, ScreenStreamNegotiation.Negotiate(client, server));
    }

    [Fact]
    public void Negotiate_jpegBinV1_skipped_when_server_does_not_support()
    {
        var client = new[] { ScreenStreamNegotiation.WebRtcV1, ScreenStreamNegotiation.JpegBinV1, ScreenStreamNegotiation.JpegV1 };
        var server = new[] { ScreenStreamNegotiation.JpegV1 };
        Assert.Equal(ScreenStreamNegotiation.JpegV1, ScreenStreamNegotiation.Negotiate(client, server));
    }

    [Fact]
    public void GetWebRtcTargetBitrate_returns_correct_bitrate_for_quality_presets()
    {
        // Preset exact boundaries
        Assert.Equal(3000, ScreenStreamNegotiation.GetWebRtcTargetBitrate(75)); // Normal
        Assert.Equal(5500, ScreenStreamNegotiation.GetWebRtcTargetBitrate(80)); // High
        Assert.Equal(9000, ScreenStreamNegotiation.GetWebRtcTargetBitrate(90)); // Best

        // Below and above preset boundary values
        Assert.Equal(3000, ScreenStreamNegotiation.GetWebRtcTargetBitrate(0));
        Assert.Equal(3000, ScreenStreamNegotiation.GetWebRtcTargetBitrate(74));
        Assert.Equal(5500, ScreenStreamNegotiation.GetWebRtcTargetBitrate(76));
        Assert.Equal(5500, ScreenStreamNegotiation.GetWebRtcTargetBitrate(79));
        Assert.Equal(9000, ScreenStreamNegotiation.GetWebRtcTargetBitrate(81));
        Assert.Equal(9000, ScreenStreamNegotiation.GetWebRtcTargetBitrate(100));
    }
}

