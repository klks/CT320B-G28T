using System.Drawing;
using CT320B.LabelDesigner.Core.Rendering;

namespace CT320B.LabelDesigner.Core.Model.Elements;

/// <summary>
/// A grid of cells rendered as lines + text. <see cref="Cells"/> is row-major
/// (<see cref="Rows"/> × <see cref="Columns"/>); missing entries render empty. Columns and rows are
/// equal-width/height for now.
/// </summary>
public sealed class TableElement : LabelElement
{
    /// <summary>Number of rows.</summary>
    public int Rows { get; set; } = 2;

    /// <summary>Number of columns.</summary>
    public int Columns { get; set; } = 2;

    /// <summary>Cell text in row-major order (index = row * Columns + col).</summary>
    public List<string> Cells { get; set; } = [];

    /// <summary>Border/line thickness in millimetres.</summary>
    public float StrokeWidthMm { get; set; } = 0.3f;

    /// <summary>Cell text size in points.</summary>
    public float FontSizePt { get; set; } = 8f;

    public override void ApplyDataBinding(Func<string, string> resolve) =>
        Cells = Cells.Select(resolve).ToList();

    public override void Render(Graphics g, RenderContext ctx)
    {
        int rows = Math.Max(1, Rows), cols = Math.Max(1, Columns);
        float x = ctx.MmToPx(XMm), y = ctx.MmToPx(YMm);
        float w = ctx.MmToPx(WidthMm), h = ctx.MmToPx(HeightMm);
        if (w < 1 || h < 1) return;

        float cellW = w / cols, cellH = h / rows;
        float stroke = Math.Max(1f, ctx.MmToPx(StrokeWidthMm));
        using var pen = new Pen(Color.Black, stroke);

        // Outer border + inner grid lines.
        g.DrawRectangle(pen, x, y, w, h);
        for (int c = 1; c < cols; c++) g.DrawLine(pen, x + c * cellW, y, x + c * cellW, y + h);
        for (int r = 1; r < rows; r++) g.DrawLine(pen, x, y + r * cellH, x + w, y + r * cellH);

        // Cell text.
        float fontPx = Math.Max(6f, (float)(FontSizePt / 72.0 * ctx.Dpi));
        using var font = new Font("Segoe UI", fontPx, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.Black);
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.Character, FormatFlags = StringFormatFlags.NoWrap,
        };
        float pad = stroke + 1;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                int i = r * cols + c;
                if (i >= Cells.Count || string.IsNullOrEmpty(Cells[i])) continue;
                var cell = new RectangleF(x + c * cellW + pad, y + r * cellH + pad,
                    cellW - 2 * pad, cellH - 2 * pad);
                g.DrawString(Cells[i], font, brush, cell, fmt);
            }
    }
}
