using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pconnect.Agent.UI;

internal enum ModernButtonStyle
{
    Primary,
    Secondary,
    Outline,
    Danger,
    Success
}

internal class ModernButton : Button
{
    private bool _isHovered;
    private bool _isPressed;
    private ModernButtonStyle _style = ModernButtonStyle.Primary;
    private int _borderRadius = 8;

    public ModernButtonStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            Invalidate();
        }
    }

    public int BorderRadius
    {
        get => _borderRadius;
        set
        {
            _borderRadius = value;
            Invalidate();
        }
    }

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Font = ThemeColors.BoldBodyFont;
        Cursor = Cursors.Hand;
        Height = 36;
        Padding = new Padding(14, 0, 14, 0);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var textSize = TextRenderer.MeasureText(Text, Font);
        int width = textSize.Width + Padding.Left + Padding.Right + 8;
        int height = Math.Max(Height, textSize.Height + 10);
        return new Size(width, height);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        Color bg;
        Color fg = ThemeColors.TextPrimary;
        Color border = Color.Transparent;

        if (!Enabled)
        {
            bg = Color.FromArgb(40, 45, 60);
            fg = ThemeColors.TextMuted;
        }
        else
        {
            switch (_style)
            {
                case ModernButtonStyle.Primary:
                    bg = _isPressed ? ThemeColors.PrimaryPressed : (_isHovered ? ThemeColors.PrimaryHover : ThemeColors.Primary);
                    fg = Color.White;
                    break;

                case ModernButtonStyle.Secondary:
                    bg = _isPressed ? Color.FromArgb(50, 58, 80) : (_isHovered ? ThemeColors.CardHover : ThemeColors.ControlBg);
                    border = ThemeColors.CardBorder;
                    fg = ThemeColors.TextPrimary;
                    break;

                case ModernButtonStyle.Outline:
                    bg = _isHovered ? Color.FromArgb(35, 42, 60) : Color.Transparent;
                    border = _isHovered ? ThemeColors.Primary : ThemeColors.CardBorder;
                    fg = _isHovered ? ThemeColors.Primary : ThemeColors.TextPrimary;
                    break;

                case ModernButtonStyle.Danger:
                    bg = _isPressed ? Color.FromArgb(185, 28, 28) : (_isHovered ? Color.FromArgb(220, 38, 38) : ThemeColors.Danger);
                    fg = Color.White;
                    break;

                case ModernButtonStyle.Success:
                    bg = _isPressed ? Color.FromArgb(21, 128, 61) : (_isHovered ? Color.FromArgb(22, 163, 74) : ThemeColors.Success);
                    fg = Color.White;
                    break;

                default:
                    bg = ThemeColors.Primary;
                    break;
            }
        }

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectangle(rect, _borderRadius);

        using (var brush = new SolidBrush(bg))
        {
            g.FillPath(brush, path);
        }

        if (border != Color.Transparent)
        {
            using var pen = new Pen(border, 1.2f);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            rect,
            fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );
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

        // Top-left
        path.AddArc(arc, 180, 90);
        // Top-right
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        // Bottom-right
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        // Bottom-left
        arc.X = rect.X;
        path.AddArc(arc, 90, 90);

        path.CloseFigure();
        return path;
    }
}
