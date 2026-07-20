using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pconnect.Agent.UI;

internal class ModernProgressBar : Control
{
    private int _value = 0;
    private int _maximum = 100;
    private string _label = string.Empty;

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, _maximum);
            Invalidate();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(1, value);
            Invalidate();
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            _label = value;
            Invalidate();
        }
    }

    public ModernProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 44;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Label and Percentage Header
        var labelRect = new Rectangle(0, 0, Width - 60, 20);
        TextRenderer.DrawText(g, _label, ThemeColors.BoldBodyFont, labelRect, ThemeColors.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        string pctText = $"{_value}%";
        var pctRect = new Rectangle(Width - 60, 0, 60, 20);
        TextRenderer.DrawText(g, pctText, ThemeColors.BoldBodyFont, pctRect, ThemeColors.TextSecondary, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        // Progress Track
        int trackY = 24;
        int trackHeight = 10;
        var trackRect = new Rectangle(0, trackY, Width, trackHeight);

        using (var trackPath = CreateRoundedRectangle(trackRect, 5))
        using (var trackBrush = new SolidBrush(ThemeColors.TrackBg))
        {
            g.FillPath(trackBrush, trackPath);
        }

        // Fill bar
        float percent = (float)_value / _maximum;
        int fillWidth = (int)(Width * percent);
        if (fillWidth > 0)
        {
            Color barColor = ThemeColors.Success;
            if (_value >= 85) barColor = ThemeColors.Danger;
            else if (_value >= 65) barColor = ThemeColors.Warning;

            var fillRect = new Rectangle(0, trackY, Math.Max(fillWidth, 10), trackHeight);
            using var fillPath = CreateRoundedRectangle(fillRect, 5);
            using var fillBrush = new SolidBrush(barColor);
            g.FillPath(fillBrush, fillPath);
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
