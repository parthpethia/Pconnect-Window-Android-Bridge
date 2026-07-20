using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pconnect.Agent.UI;

internal class ModernTabControl : Control
{
    private readonly List<string> _tabs = new();
    private int _selectedIndex = 0;

    public event EventHandler? SelectedIndexChanged;

    public List<string> Tabs => _tabs;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value >= 0 && value < _tabs.Count && _selectedIndex != value)
            {
                _selectedIndex = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ModernTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 40;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && _tabs.Count > 0)
        {
            int tabWidth = Width / _tabs.Count;
            int clickedIndex = e.X / Math.Max(1, tabWidth);
            if (clickedIndex >= 0 && clickedIndex < _tabs.Count)
            {
                SelectedIndex = clickedIndex;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_tabs.Count == 0) return;

        int tabWidth = Width / _tabs.Count;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool isSelected = (i == _selectedIndex);
            var tabRect = new Rectangle(i * tabWidth, 0, tabWidth, Height - 3);

            Color fg = isSelected ? ThemeColors.Primary : ThemeColors.TextSecondary;
            Font font = isSelected ? ThemeColors.BoldBodyFont : ThemeColors.BodyFont;

            TextRenderer.DrawText(g, _tabs[i], font, tabRect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (isSelected)
            {
                var indicatorRect = new Rectangle((i * tabWidth) + (tabWidth / 6), Height - 3, (tabWidth * 2) / 3, 3);
                using var brush = new SolidBrush(ThemeColors.Primary);
                using var path = CreateRoundedRectangle(indicatorRect, 1);
                g.FillPath(brush, path);
            }
        }

        // Bottom border line
        using var linePen = new Pen(ThemeColors.CardBorder, 1);
        g.DrawLine(linePen, 0, Height - 1, Width, Height - 1);
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
