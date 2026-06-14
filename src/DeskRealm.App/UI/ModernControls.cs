using System.Drawing.Drawing2D;

namespace DeskRealm.App.UI;

internal sealed class RoundedPanel : Panel
{
    public Color FillColor { get; set; } = Color.FromArgb(9, 32, 47);
    public Color BorderColor { get; set; } = Color.FromArgb(24, 92, 107);
    public int BorderThickness { get; set; } = 1;
    public int Radius { get; set; } = 18;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var parentBrush = new SolidBrush(Parent?.BackColor ?? FillColor);
        e.Graphics.FillRectangle(parentBrush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var inset = Math.Max(1, BorderThickness);
        var rect = new Rectangle(inset, inset, Math.Max(1, Width - (inset * 2) - 1), Math.Max(1, Height - (inset * 2) - 1));
        using var path = CreateRoundRectangle(rect, Radius);
        using var fill = new SolidBrush(FillColor);
        e.Graphics.FillPath(fill, path);

        if (BorderThickness > 0)
        {
            using var border = new Pen(BorderColor, BorderThickness);
            e.Graphics.DrawPath(border, path);
        }

        base.OnPaint(e);
    }

    private static GraphicsPath CreateRoundRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return path;
        }

        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModernButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public Color FillColor { get; set; } = Color.FromArgb(13, 49, 58);
    public Color HoverFillColor { get; set; } = Color.FromArgb(18, 71, 81);
    public Color PressedFillColor { get; set; } = Color.FromArgb(20, 92, 102);
    public Color AccentColor { get; set; } = Color.FromArgb(43, 214, 222);
    public Color MutedAccentColor { get; set; } = Color.FromArgb(24, 92, 107);
    public Color DisabledFillColor { get; set; } = Color.FromArgb(18, 28, 36);
    public Color DisabledBorderColor { get; set; } = Color.FromArgb(47, 69, 78);
    public Color DisabledTextColor { get; set; } = Color.FromArgb(116, 144, 154);
    public int Radius { get; set; } = 16;
    public bool Selected { get; set; }

    public ModernButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.Selectable,
            true);
        DoubleBuffered = true;
        TabStop = false;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        ForeColor = Color.FromArgb(231, 251, 255);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressed)
        {
            _pressed = false;
            Invalidate();
        }

        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        Invalidate();
        base.OnTextChanged(e);
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        Invalidate();
        base.OnForeColorChanged(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background = new SolidBrush(Parent?.BackColor ?? Color.FromArgb(5, 16, 25));
        e.Graphics.FillRectangle(background, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var fillColor = !Enabled
            ? DisabledFillColor
            : _pressed ? PressedFillColor
            : _hovered || Selected ? HoverFillColor
            : FillColor;
        var borderColor = !Enabled
            ? DisabledBorderColor
            : Selected ? AccentColor
            : MutedAccentColor;
        var textColor = !Enabled ? DisabledTextColor : ForeColor;

        using var path = CreateRoundRectangle(rect, Radius);
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(borderColor, Selected ? 2 : 1);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            rect,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static GraphicsPath CreateRoundRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return path;
        }

        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class PillLabel : Control
{
    public Color FillColor { get; set; } = Color.FromArgb(8, 27, 39);
    public Color BorderColor { get; set; } = Color.FromArgb(24, 92, 107);
    public Color TextColor { get; set; } = Color.FromArgb(231, 251, 255);
    public int Radius { get; set; } = 13;

    public PillLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using var parentBrush = new SolidBrush(Parent?.BackColor ?? Color.Transparent);
        pevent.Graphics.FillRectangle(parentBrush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var rect = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = CreateRoundRectangle(rect, Radius);
        using var fill = new SolidBrush(FillColor);
        using var border = new Pen(BorderColor, 1);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, TextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath CreateRoundRectangle(Rectangle rectangle, int radius)
    {
        var path = new GraphicsPath();
        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return path;
        }

        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height)));
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
