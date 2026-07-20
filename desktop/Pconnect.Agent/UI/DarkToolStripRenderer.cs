using System.Drawing;
using System.Windows.Forms;

namespace Pconnect.Agent.UI;

internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
{
    public DarkToolStripRenderer() : base(new DarkColorTable())
    {
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected ? ThemeColors.TextPrimary : ThemeColors.TextSecondary;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(ThemeColors.CardBorder);
        e.Graphics.DrawLine(pen, 28, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => ThemeColors.Surface;
        public override Color MenuStripGradientEnd => ThemeColors.Surface;
        public override Color MenuItemSelected => ThemeColors.CardHover;
        public override Color MenuItemSelectedGradientBegin => ThemeColors.CardHover;
        public override Color MenuItemSelectedGradientEnd => ThemeColors.CardHover;
        public override Color MenuItemBorder => ThemeColors.CardBorder;
        public override Color MenuBorder => ThemeColors.CardBorder;
        public override Color ToolStripDropDownBackground => ThemeColors.Surface;
        public override Color ImageMarginGradientBegin => ThemeColors.Surface;
        public override Color ImageMarginGradientMiddle => ThemeColors.Surface;
        public override Color ImageMarginGradientEnd => ThemeColors.Surface;
        public override Color CheckBackground => ThemeColors.ControlBg;
        public override Color CheckSelectedBackground => ThemeColors.Primary;
        public override Color CheckPressedBackground => ThemeColors.PrimaryPressed;
    }
}
