using Xunit;
using Pconnect.Agent.Services;

namespace Pconnect.Agent.Tests;

public class KeyComboTests
{
    [Theory]
    [InlineData("alt", 0x12, false)]
    [InlineData("tab", 0x09, false)]
    [InlineData("ctrl", 0x11, false)]
    [InlineData("shift", 0x10, false)]
    [InlineData("win", 0x5B, true)]
    [InlineData("lalt", 0x12, false)]
    [InlineData("ralt", 0xA5, true)]
    [InlineData("cmd", 0x5B, true)]
    [InlineData("a", 0x41, false)]
    [InlineData("z", 0x5A, false)]
    [InlineData("0", 0x30, false)]
    [InlineData("9", 0x39, false)]
    [InlineData("f4", 0x73, false)]
    [InlineData("esc", 0x1B, false)]
    [InlineData("volumeup", 0xAF, true)]
    [InlineData("vol_up", 0xAF, true)]
    [InlineData("volumedown", 0xAE, true)]
    [InlineData("vol_down", 0xAE, true)]
    [InlineData("mute", 0xAD, true)]
    [InlineData("volumemute", 0xAD, true)]
    [InlineData("play_pause", 0xB3, true)]
    public void ResolveKey_ValidKeys_ReturnsExpectedVkAndExtended(string keyName, ushort expectedVk, bool expectedExt)
    {
        var result = KeyComboService.ResolveKey(keyName);
        Assert.NotNull(result);
        Assert.Equal(expectedVk, result!.Value.vk);
        Assert.Equal(expectedExt, result.Value.extended);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void ResolveKey_UnknownKey_ReturnsNull()
    {
        var result = KeyComboService.ResolveKey("invalid_key_name_123");
        Assert.Null(result);
    }

    [Fact]
    public void Execute_NullOrEmptyList_ReturnsFalse()
    {
        Assert.False(KeyComboService.Execute(null!));
        Assert.False(KeyComboService.Execute(Array.Empty<string>()));
    }

    [Fact]
    public void Execute_UnknownKeyInCombo_ReturnsFalse()
    {
        Assert.False(KeyComboService.Execute(new[] { "alt", "unknown_key_xyz" }));
    }
}
