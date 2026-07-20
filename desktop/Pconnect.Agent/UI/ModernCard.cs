using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pconnect.Agent.UI;

internal class ModernCard : Panel
{
    private int _borderRadius = 10;
    private Color _borderColor = ThemeColors.CardBorder;
    private Color _cardBgColor = ThemeColors.CardBg;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;

    public int BorderRadius
    {
        get => _borderRadius;
        set { _borderRadius = value; Invalidate(); }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set { _borderColor = value; Invalidate(); }
    }

    public Color CardBgColor
    {
        get => _cardBgColor;
        set { _cardBgColor = value; Invalidate(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; Invalidate(); }
    }

    public ModernCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Padding = new Padding(16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, _borderRadius);

        using (var brush = new SolidBrush(_cardBgColor))
        {
            g.FillPath(brush, path);
        }

        if (_borderColor != Color.Transparent)
        {
            using var pen = new Pen(_borderColor, 1f);
            g.DrawPath(pen, path);
        }

        int currentY = Padding.Top;
        if (!string.IsNullOrEmpty(_title))
        {
            var titleRect = new Rectangle(Padding.Left, currentY, Width - Padding.Horizontal, 24);
            TextRenderer.DrawText(g, _title, ThemeColors.TitleFont, titleRect, ThemeColors.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            currentY += 24;
        }

        if (!string.IsNullOrEmpty(_subtitle))
        {
            var subRect = new Rectangle(Padding.Left, currentY, Width - Padding.Horizontal, 18);
            TextRenderer.DrawText(g, _subtitle, ThemeColors.SubtitleFont, subRect, ThemeColors.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int diameter = radius * 2;
        var arc = new Rectangle(rect.X, rect.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.X;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
