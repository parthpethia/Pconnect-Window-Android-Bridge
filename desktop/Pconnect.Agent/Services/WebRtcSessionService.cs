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

    public bool HostOnly { get; }

    private readonly Action<byte[]>? _onFileXferPacket;

    public WebRtcSessionService(
        Action<byte[]> onInputPacket,
        Action<string> onIceCandidate,
        Action onConnected,
        Action<string> onFailed,
        bool hostOnly = false,
        Action<byte[]>? onFileXferPacket = null)
    {
        _onInputPacket = onInputPacket;
        _onFileXferPacket = onFileXferPacket;
        _onIceCandidate = onIceCandidate;
        _onConnected = onConnected;
        _onFailed = onFailed;
        HostOnly = hostOnly;

        var iceServers = new List<RTCIceServer>();
        if (!hostOnly)
        {
            iceServers.Add(new RTCIceServer { urls = "stun:stun.l.google.com:19302" });
        }

        var config = new RTCConfiguration
        {
            iceServers = iceServers
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
            else if (channel.label == "filexfer")
            {
                channel.onmessage += (chan, protocol, data) =>
                {
                    _onFileXferPacket?.Invoke(data);
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

        if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(frameBytes, out var segment) && segment.Array != null)
        {
            if (segment.Offset == 0 && segment.Count == segment.Array.Length)
            {
                _pc.SendVideo(durationMs, segment.Array);
                return;
            }
        }

        byte[] array = frameBytes.ToArray();
        _pc.SendVideo(durationMs, array);
    }

    public double GetLastLossFraction()
    {
        return 0.0;
    }

    public double GetLastRttMs()
    {
        return 10.0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _pc.Close("normal");
    }
}
