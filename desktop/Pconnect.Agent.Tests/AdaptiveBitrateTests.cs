using Pconnect.Agent.Services;
using Xunit;

namespace Pconnect.Agent.Tests;

[Collection("StaticStateTests")]
public sealed class AdaptiveBitrateTests
{
    [Fact]
    public void AimdBitrateController_increases_bitrate_when_loss_is_low()
    {
        var controller = new AimdBitrateController(3000, 5500, 800);
        int newRate = controller.Step(0.005, 30.0); // 0.5% loss, 30ms RTT
        Assert.True(newRate > 3000, "Bitrate should increase under low loss");
        Assert.True(newRate <= 5500, "Bitrate should not exceed ceiling");
    }

    [Fact]
    public void AimdBitrateController_holds_bitrate_when_loss_is_moderate()
    {
        var controller = new AimdBitrateController(3000, 5500, 800);
        int newRate = controller.Step(0.03, 50.0); // 3% loss
        Assert.Equal(3000, newRate);
    }

    [Fact]
    public void AimdBitrateController_decreases_bitrate_when_loss_is_high()
    {
        var controller = new AimdBitrateController(3000, 5500, 800);
        int newRate = controller.Step(0.08, 60.0); // 8% loss
        Assert.Equal(2250, newRate); // 3000 * 0.75 = 2250
    }

    [Fact]
    public void AimdBitrateController_triggers_fallback_when_forced_below_floor()
    {
        var controller = new AimdBitrateController(1000, 5500, 800);
        int step1 = controller.Step(0.10, 100.0); // 1000 * 0.75 = 750 (< 800)
        Assert.Equal(-1, step1);
    }

    [Fact]
    public void AimdBitrateController_enforces_cooldown_after_decrease()
    {
        var controller = new AimdBitrateController(4000, 5500, 800);
        int decreased = controller.Step(0.08, 50.0); // 4000 -> 3000
        Assert.Equal(3000, decreased);

        // Immediate next step under 0% loss should hold steady due to active 1.5s cooldown
        int cooldownStep = controller.Step(0.0, 30.0);
        Assert.Equal(3000, cooldownStep);
    }

    [Fact]
    public void AimdBitrateController_ramps_up_after_cooldown_expires()
    {
        var controller = new AimdBitrateController(4000, 5500, 800);
        int decreased = controller.Step(0.08, 50.0); // 4000 -> 3000
        Assert.Equal(3000, decreased);

        // Wait 1.55 seconds for post-decrease cooldown to expire
        System.Threading.Thread.Sleep(1550);

        int rampedUp = controller.Step(0.0, 20.0); // 0% loss, 20ms RTT
        Assert.True(rampedUp > 3000, "Bitrate should ramp up after cooldown expires");
        Assert.Equal(3225, rampedUp);
    }

    [Fact]
    public void H264EncoderService_RequestKeyframe_sets_pending_keyframe()
    {
        H264EncoderService.ForceInitializeSuccess = true;
        try
        {
            using var encoder = new H264EncoderService();
            encoder.Initialize(1280, 720, 30, 3000);
            encoder.RequestKeyframe();
            var result = encoder.Encode(new byte[1280 * 720 * 4], 1280, 720, false);
            Assert.True(result.IsEmpty);
        }
        finally
        {
            H264EncoderService.ForceInitializeSuccess = false;
        }
    }

    [Fact]
    public void H264EncoderService_UpdateBitrate_returns_true_in_mocked_mode()
    {
        H264EncoderService.ForceInitializeSuccess = true;
        try
        {
            using var encoder = new H264EncoderService();
            encoder.Initialize(1280, 720, 30, 3000);
            Assert.True(encoder.UpdateBitrate(4500));
            Assert.Equal(4500, encoder.CurrentBitrateKbps);
        }
        finally
        {
            H264EncoderService.ForceInitializeSuccess = false;
        }
    }
}
