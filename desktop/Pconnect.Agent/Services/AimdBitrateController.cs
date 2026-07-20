namespace Pconnect.Agent.Services;

/// <summary>
/// Lightweight AIMD (Additive Increase, Multiplicative Decrease) bitrate controller for WebRTC streaming.
/// Evaluates live network stats (packet loss fraction, RTT) every 250-500ms and targets bitrate adjustments.
/// Enforces preset ceilings and the 800 Kbps minimum floor.
/// </summary>
internal sealed class AimdBitrateController
{
    public int CurrentBitrateKbps { get; private set; }
    public int BitrateCeilingKbps { get; }
    public int BitrateFloorKbps { get; }

    public AimdBitrateController(int initialBitrateKbps, int bitrateCeilingKbps, int bitrateFloorKbps = ScreenStreamNegotiation.MinBitrateFloorKbps)
    {
        BitrateCeilingKbps = bitrateCeilingKbps;
        BitrateFloorKbps = bitrateFloorKbps;
        CurrentBitrateKbps = Math.Clamp(initialBitrateKbps, bitrateFloorKbps, bitrateCeilingKbps);
    }

    /// <summary>
    /// Evaluates current network metrics and calculates the next target bitrate in Kbps.
    /// </summary>
    /// <param name="lossFraction">Packet loss fraction (0.0 to 1.0, e.g. 0.03 = 3% loss)</param>
    /// <param name="rttMs">Round-trip time in milliseconds</param>
    /// <param name="rttRising">Whether RTT trend is sharply rising</param>
    /// <returns>New target bitrate in Kbps, or -1 if forced below 800 Kbps floor (triggers JPEG fallback)</returns>
    private long _lastDecreaseTicks = 0;
    private const long CooldownMs = 1500;

    public int Step(double lossFraction, double rttMs, bool rttRising = false)
    {
        long now = Environment.TickCount64;

        if (lossFraction > 0.05)
        {
            // Severe loss (> 5%): Multiplicative decrease (-25%) and start cooldown
            _lastDecreaseTicks = now;
            int decreased = (int)(CurrentBitrateKbps * 0.75);
            CurrentBitrateKbps = decreased;
        }
        else if (lossFraction >= 0.01 || rttRising || rttMs > 250 || (now - _lastDecreaseTicks < CooldownMs))
        {
            // Moderate loss (1%-5%), rising latency, or active post-decrease cooldown (1.5s): Hold steady
        }
        else
        {
            // Low loss (< 1%) and stable RTT: Additive increase (+7.5%, minimum +150 Kbps)
            int increment = Math.Max(150, (int)(CurrentBitrateKbps * 0.075));
            CurrentBitrateKbps = Math.Min(BitrateCeilingKbps, CurrentBitrateKbps + increment);
        }

        if (CurrentBitrateKbps < BitrateFloorKbps)
        {
            // Forced below 800 Kbps floor: signal fallback to JPEG mode
            return -1;
        }

        return CurrentBitrateKbps;
    }
}
