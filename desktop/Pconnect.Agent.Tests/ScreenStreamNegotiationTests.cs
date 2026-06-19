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
}
