using System.Drawing;

namespace Pconnect.Agent.UI;

internal static class ThemeColors
{
    // Backgrounds
    public static readonly Color Background = DesignTokens.BgBase;
    public static readonly Color Surface = DesignTokens.BgElevated1;
    public static readonly Color CardBg = DesignTokens.BgElevated1;
    public static readonly Color CardHover = DesignTokens.BgElevated2;
    public static readonly Color CardBorder = DesignTokens.BorderSubtle;
    
    // Accent Colors
    public static readonly Color Primary = DesignTokens.Primary;
    public static readonly Color PrimaryHover = DesignTokens.PrimaryHover;
    public static readonly Color PrimaryPressed = DesignTokens.PrimaryPressed;
    
    public static readonly Color Success = DesignTokens.Success;
    public static readonly Color Warning = DesignTokens.Warning;
    public static readonly Color Danger = DesignTokens.Danger;
    public static readonly Color Info = DesignTokens.Info;

    // Text & Foregrounds
    public static readonly Color TextPrimary = DesignTokens.TextPrimary;
    public static readonly Color TextSecondary = DesignTokens.TextSecondary;
    public static readonly Color TextMuted = DesignTokens.TextDisabled;
    
    // Controls
    public static readonly Color ControlBg = DesignTokens.BgElevated2;
    public static readonly Color TrackBg = DesignTokens.BgElevated3;

    public static readonly Font HeaderFont = DesignTokens.HeaderFont;
    public static readonly Font TitleFont = DesignTokens.TitleFont;
    public static readonly Font SubtitleFont = DesignTokens.SubtitleFont;
    public static readonly Font BodyFont = DesignTokens.BodyFont;
    public static readonly Font BoldBodyFont = DesignTokens.BoldBodyFont;
    public static readonly Font SmallFont = DesignTokens.SmallFont;
}
