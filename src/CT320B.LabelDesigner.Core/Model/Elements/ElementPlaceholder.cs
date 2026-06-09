using System.Drawing;
using System.Drawing.Drawing2D;

namespace CT320B.LabelDesigner.Core.Model.Elements;

/// <summary>Draws a dashed placeholder box with a caption — used by code/image elements when their
/// content is missing or invalid, so the canvas shows the element without throwing or printing junk.</summary>
internal static class ElementPlaceholder
{
    public static void Draw(Graphics g, RectangleF rect, string caption)
    {
        if (rect.Width < 1 || rect.Height < 1) return;
        using var pen = new Pen(Color.Gray) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
        float fontPx = Math.Max(6f, Math.Min(rect.Height * 0.4f, rect.Width / Math.Max(1, caption.Length)));
        using var font = new Font("Segoe UI", fontPx, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.Gray);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(caption, font, brush, rect, fmt);
    }
}
