using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pconnect.Agent.UI;

internal class ModernSlider : Control
{
    private int _value = 50;
    private int _minimum = 0;
    private int _maximum = 100;
    private bool _isDragging;
    private string _unit = "%";

    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            int val = Math.Clamp(value, _minimum, _maximum);
            if (_value != val)
            {
                _value = val;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            Invalidate();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            Invalidate();
        }
    }

    public string Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            Invalidate();
        }
    }

    public ModernSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 32;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _isDragging = true;
            UpdateValueFromMouse(e.X);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDragging)
        {
            UpdateValueFromMouse(e.X);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isDragging = false;
    }

    private void UpdateValueFromMouse(int mouseX)
    {
        int trackMargin = 16;
        int trackWidth = Width - (trackMargin * 2);
        if (trackWidth <= 0) return;

        float ratio = (float)(mouseX - trackMargin) / trackWidth;
        ratio = Math.Clamp(ratio, 0f, 1f);
        Value = (int)Math.Round(_minimum + (ratio * (_maximum - _minimum)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        int trackMargin = 16;
        int trackY = Height / 2;
        int trackHeight = 6;
        int trackWidth = Width - (trackMargin * 2);

        // Track background
        var trackRect = new Rectangle(trackMargin, trackY - (trackHeight / 2), trackWidth, trackHeight);
        using (var trackPath = CreateRoundedRectangle(trackRect, trackHeight / 2))
        using (var trackBrush = new SolidBrush(ThemeColors.TrackBg))
        {
            g.FillPath(trackBrush, trackPath);
        }

        // Active track fill
        float percent = (_maximum > _minimum) ? (float)(_value - _minimum) / (_maximum - _minimum) : 0f;
        int fillWidth = (int)(trackWidth * percent);
        if (fillWidth > 0)
        {
            var fillRect = new Rectangle(trackMargin, trackY - (trackHeight / 2), Math.Max(fillWidth, trackHeight), trackHeight);
            using var fillPath = CreateRoundedRectangle(fillRect, trackHeight / 2);
            using var fillBrush = new SolidBrush(ThemeColors.Primary);
            g.FillPath(fillBrush, fillPath);
        }

        // Thumb handle
        int thumbRadius = 8;
        int thumbX = trackMargin + fillWidth;
        var thumbRect = new Rectangle(thumbX - thumbRadius, trackY - thumbRadius, thumbRadius * 2, thumbRadius * 2);

        using (var thumbBrush = new SolidBrush(Color.White))
        {
            g.FillEllipse(thumbBrush, thumbRect);
        }

        using (var thumbBorder = new Pen(ThemeColors.Primary, 2f))
        {
            g.DrawEllipse(thumbBorder, thumbRect);
        }

        // Value text
        string valText = $"{_value}{_unit}";
        var textRect = new Rectangle(Width - 50, 0, 48, Height);
        TextRenderer.DrawText(g, valText, ThemeColors.BoldBodyFont, textRect, ThemeColors.TextSecondary, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
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
