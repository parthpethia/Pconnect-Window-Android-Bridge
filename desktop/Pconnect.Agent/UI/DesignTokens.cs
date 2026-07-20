using System.Drawing;

namespace Pconnect.Agent.UI;

internal static class DesignTokens
{
    // Core Accent & Glow Colors (#6C5CE7 Indigo Base)
    public static readonly Color Primary = Color.FromArgb(108, 92, 231);
    public static readonly Color PrimaryHover = Color.FromArgb(90, 75, 209);
    public static readonly Color PrimaryPressed = Color.FromArgb(75, 63, 184);
    public static readonly Color PrimaryGlow = Color.FromArgb(90, 108, 92, 231);

    // Dark-First Surfaces
    public static readonly Color BgBase = Color.FromArgb(18, 18, 22);        // #121216
    public static readonly Color BgElevated1 = Color.FromArgb(26, 26, 32);    // #1A1A20 Cards
    public static readonly Color BgElevated2 = Color.FromArgb(35, 35, 48);    // #232330 Modals
    public static readonly Color BgElevated3 = Color.FromArgb(44, 44, 58);    // #2C2C3A Popovers

    // Borders & Dividers
    public static readonly Color BorderSubtle = Color.FromArgb(25, 255, 255, 255); // 10% white
    public static readonly Color BorderStrong = Color.FromArgb(50, 255, 255, 255); // 20% white

    // Text Hierarchy
    public static readonly Color TextPrimary = Color.FromArgb(248, 249, 250);   // #F8F9FA
    public static readonly Color TextSecondary = Color.FromArgb(160, 160, 176); // #A0A0B0
    public static readonly Color TextDisabled = Color.FromArgb(92, 92, 104);    // #5C5C68

    // Semantic Status Colors
    public static readonly Color Success = Color.FromArgb(0, 184, 148);  // #00B894
    public static readonly Color Warning = Color.FromArgb(253, 203, 110); // #FDCB6E
    public static readonly Color Danger = Color.FromArgb(255, 118, 117);  // #FF7675
    public static readonly Color Info = Color.FromArgb(116, 185, 255);   // #74B9FF

    // Standardized Fonts
    public static readonly Font HeaderFont = new("Segoe UI", 15F, FontStyle.Bold);
    public static readonly Font TitleFont = new("Segoe UI", 11F, FontStyle.Bold);
    public static readonly Font SubtitleFont = new("Segoe UI", 9.5F, FontStyle.Regular);
    public static readonly Font BodyFont = new("Segoe UI", 9F, FontStyle.Regular);
    public static readonly Font BoldBodyFont = new("Segoe UI", 9F, FontStyle.Bold);
    public static readonly Font SmallFont = new("Segoe UI", 8F, FontStyle.Regular);
    public static readonly Font PinFont = new("Segoe UI", 32F, FontStyle.Bold);
}
