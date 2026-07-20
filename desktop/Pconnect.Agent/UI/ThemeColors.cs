using System.Drawing;

namespace Pconnect.Agent.UI;

internal static class ThemeColors
{
    // Backgrounds
    public static readonly Color Background = Color.FromArgb(15, 17, 23);       // #0F1117
    public static readonly Color Surface = Color.FromArgb(24, 27, 38);          // #181B26
    public static readonly Color CardBg = Color.FromArgb(30, 35, 50);           // #1E2332
    public static readonly Color CardHover = Color.FromArgb(38, 44, 63);        // #262C3F
    public static readonly Color CardBorder = Color.FromArgb(45, 52, 75);       // #2D344B
    
    // Accent Colors
    public static readonly Color Primary = Color.FromArgb(59, 130, 246);        // #3B82F6 (Electric Blue)
    public static readonly Color PrimaryHover = Color.FromArgb(37, 99, 235);    // #2563EB
    public static readonly Color PrimaryPressed = Color.FromArgb(29, 78, 216);  // #1D4ED8
    
    public static readonly Color Success = Color.FromArgb(34, 197, 94);        // #22C55E (Neon Green)
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);       // #F59E0B (Amber)
    public static readonly Color Danger = Color.FromArgb(239, 68, 68);         // #EF4444 (Coral Red)
    public static readonly Color Info = Color.FromArgb(14, 165, 233);          // #0EA5E9 (Sky Blue)

    // Text & Foregrounds
    public static readonly Color TextPrimary = Color.FromArgb(243, 244, 246);   // #F3F4F6
    public static readonly Color TextSecondary = Color.FromArgb(156, 163, 175); // #9CA3AF
    public static readonly Color TextMuted = Color.FromArgb(107, 114, 128);     // #6B7280
    
    // Controls
    public static readonly Color ControlBg = Color.FromArgb(36, 42, 59);        // #242A3B
    public static readonly Color TrackBg = Color.FromArgb(40, 48, 68);          // #283044

    public static readonly Font HeaderFont = new("Segoe UI", 15F, FontStyle.Bold);
    public static readonly Font TitleFont = new("Segoe UI", 11F, FontStyle.Bold);
    public static readonly Font SubtitleFont = new("Segoe UI", 9.5F, FontStyle.Regular);
    public static readonly Font BodyFont = new("Segoe UI", 9F, FontStyle.Regular);
    public static readonly Font BoldBodyFont = new("Segoe UI", 9F, FontStyle.Bold);
    public static readonly Font SmallFont = new("Segoe UI", 8F, FontStyle.Regular);
}
