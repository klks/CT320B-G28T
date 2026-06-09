using System.Drawing.Drawing2D;

namespace CT320B.LabelDesigner.Controls;

/// <summary>A small filled circle used as a connection indicator (gray/orange/green/red).</summary>
public sealed class StatusLight : Control
{
    private Color _color = Color.Gray;

    public StatusLight()
    {
        Size = new Size(14, 14);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    /// <summary>Sets the lamp colour and repaints.</summary>
    public void SetColor(Color color)
    {
        if (_color == color) return;
        _color = color;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        using var fill = new SolidBrush(_color);
        using var edge = new Pen(Color.FromArgb(90, Color.Black));
        e.Graphics.FillEllipse(fill, r);
        e.Graphics.DrawEllipse(edge, r);
    }
}
