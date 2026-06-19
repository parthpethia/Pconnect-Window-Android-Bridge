using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace Pconnect.Agent.Services;

internal sealed class WebRtcSessionService : IDisposable
{
    private readonly RTCPeerConnection _pc;
    private readonly Action<byte[]> _onInputPacket;
    private readonly Action<string> _onIceCandidate;
    private readonly Action _onConnected;
    private readonly Action<string> _onFailed;
    private bool _isDisposed;

    public WebRtcSessionService(
        Action<byte[]> onInputPacket,
        Action<string> onIceCandidate,
        Action onConnected,
        Action<string> onFailed)
    {
        _onInputPacket = onInputPacket;
        _onIceCandidate = onIceCandidate;
        _onConnected = onConnected;
        _onFailed = onFailed;

        var config = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer>() // host candidates only (LAN-only)
        };

        _pc = new RTCPeerConnection(config);

        // Track setup: Send-only H.264 video track
        var videoFormat = new VideoFormat(VideoCodecsEnum.H264, 102); // Payload type 102 is standard for H264
        var videoTrack = new MediaStreamTrack(videoFormat, MediaStreamStatusEnum.SendOnly);
        _pc.addTrack(videoTrack);

        // Data channel setup: handle client-initiated data channel
        _pc.ondatachannel += (channel) =>
        {
            if (channel.label == "input")
            {
                channel.onmessage += (chan, protocol, data) =>
                {
                    _onInputPacket(data);
                };
            }
        };

        // ICE candidate callback
        _pc.onicecandidate += (candidate) =>
        {
            if (candidate != null && !string.IsNullOrEmpty(candidate.candidate))
            {
                _onIceCandidate(candidate.candidate);
            }
        };

        // Connection state callback
        _pc.onconnectionstatechange += (state) =>
        {
            Console.WriteLine($"[WebRtcSession] ICE Connection State: {state}");
            if (state == RTCPeerConnectionState.connected)
            {
                _onConnected();
            }
            else if (state == RTCPeerConnectionState.failed)
            {
                _onFailed("ICE connection failed.");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                _onFailed("ICE connection closed.");
            }
        };
    }

    public async Task<string> ProcessOfferAndCreateAnswer(string offerSdp)
    {
        var parsedOffer = new RTCSessionDescriptionInit
        {
            sdp = offerSdp,
            type = RTCSdpType.offer
        };
        var res = _pc.setRemoteDescription(parsedOffer);
        if (res != SetDescriptionResultEnum.OK)
        {
            throw new Exception($"Failed to set remote description. Result = {res}");
        }

        var answer = _pc.createAnswer(null);
        await _pc.setLocalDescription(answer);
        return answer.sdp.ToString();
    }

    public void AddIceCandidate(string candidate, string? sdpMid, int sdpMLineIndex)
    {
        var candidateInit = new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid ?? "0",
            sdpMLineIndex = (ushort)sdpMLineIndex
        };
        _pc.addIceCandidate(candidateInit);
    }

    public void SendVideoFrame(ReadOnlyMemory<byte> frameBytes, uint durationMs)
    {
        if (_isDisposed || _pc.connectionState != RTCPeerConnectionState.connected)
        {
            return;
        }

        byte[] array = frameBytes.ToArray();
        _pc.SendVideo(durationMs, array);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _pc.Close("normal");
    }
}
