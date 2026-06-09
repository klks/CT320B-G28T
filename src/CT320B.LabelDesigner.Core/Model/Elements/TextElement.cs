using System.Drawing;
using CT320B.LabelDesigner.Core.Rendering;

namespace CT320B.LabelDesigner.Core.Model.Elements;

/// <summary>Horizontal alignment of text within its bounds.</summary>
public enum TextAlignment { Left, Center, Right }

/// <summary>
/// A run of text laid out within the element's bounds and drawn with GDI+. Font size is in points;
/// the renderer sets the target bitmap's resolution so points map to the right physical size at the
/// printer's dot pitch. Minimal Phase-1 version; Phase 5 adds wrap/auto-size/vertical alignment etc.
/// </summary>
public sealed class TextElement : LabelElement
{
    /// <summary>The text to render.</summary>
    public string Text { get; set; } = "";

    /// <summary>Font family name (e.g. "Arial"). Falls back to a generic family if unavailable.</summary>
    public string FontFamily { get; set; } = "Arial";

    /// <summary>Font size in typographic points.</summary>
    public float FontSizePt { get; set; } = 10f;

    /// <summary>Bold weight.</summary>
    public bool Bold { get; set; }

    /// <summary>Italic style.</summary>
    public bool Italic { get; set; }

    /// <summary>Text colour.</summary>
    public Color Color { get; set; } = Color.Black;

    /// <summary>Horizontal alignment within the bounds.</summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    /// <summary>Whether to wrap text to the bounds width.</summary>
    public bool Wrap { get; set; } = true;

    public override void Render(Graphics g, RenderContext ctx)
    {
        if (string.IsNullOrEmpty(Text)) return;

        var rect = new RectangleF(
            ctx.MmToPx(XMm), ctx.MmToPx(YMm), ctx.MmToPx(WidthMm), ctx.MmToPx(HeightMm));

        FontStyle style = FontStyle.Regular;
        if (Bold) style |= FontStyle.Bold;
        if (Italic) style |= FontStyle.Italic;

        using var font = new Font(FontFamily, FontSizePt, style, GraphicsUnit.Point);
        using var brush = new SolidBrush(Color);
        using var format = new StringFormat
        {
            Alignment = Alignment switch
            {
                TextAlignment.Center => StringAlignment.Center,
                TextAlignment.Right => StringAlignment.Far,
                _ => StringAlignment.Near,
            },
            LineAlignment = StringAlignment.Near,
            FormatFlags = Wrap ? 0 : StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None,
        };

        if (rect.Width <= 0 || rect.Height <= 0)
            g.DrawString(Text, font, brush, rect.Location, format);   // unbounded: draw at origin
        else
            g.DrawString(Text, font, brush, rect, format);
    }

    /// <summary>
    /// Resizes the element so its box fits the current text at the current font/style, keeping the
    /// element centred where it is (so any rotation, which is about the centre, is unaffected). With
    /// <see cref="Wrap"/> off the box fits the text in both dimensions (honouring explicit line
    /// breaks); with wrap on the width is kept and only the height is re-fitted to the wrapped text —
    /// so shrinking the font shrinks the box (and re-flows the lines) instead of leaving stale gaps.
    /// No-op for empty/whitespace text.
    /// </summary>
    public void FitToContent()
    {
        if (string.IsNullOrWhiteSpace(Text)) return;

        FontStyle style = FontStyle.Regular;
        if (Bold) style |= FontStyle.Bold;
        if (Italic) style |= FontStyle.Italic;

        using var font = new Font(FontFamily, FontSizePt, style, GraphicsUnit.Point);
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        // Measure at the Graphics' own dpi and convert back with it. Wrapping is dpi-independent
        // (text-px/box-px cancels the dpi factor), so the line breaks match the renderer's.
        float dpi = g.DpiX;
        float ToMm(float px) => px / dpi * 25.4f;
        float ToPx(float mm) => mm * dpi / 25.4f;

        using var format = new StringFormat
        {
            FormatFlags = Wrap ? 0 : StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None,
        };

        const float padMm = 0.4f;   // small guard against right/bottom edge clipping
        if (Wrap && WidthMm > 0)
        {
            SizeF s = g.MeasureString(Text, font, new SizeF(ToPx(WidthMm), 100_000f), format);
            SetSizeKeepingCentre(WidthMm, ToMm(s.Height) + padMm);
        }
        else
        {
            SizeF s = g.MeasureString(Text, font, new SizeF(100_000f, 100_000f), format);
            SetSizeKeepingCentre(ToMm(s.Width) + padMm, ToMm(s.Height) + padMm);
        }
    }

    public override void ApplyDataBinding(Func<string, string> resolve) =>
        Text = resolve(Text);

    private void SetSizeKeepingCentre(float wMm, float hMm)
    {
        float cx = XMm + WidthMm / 2f, cy = YMm + HeightMm / 2f;
        WidthMm = wMm;
        HeightMm = hMm;
        XMm = cx - wMm / 2f;
        YMm = cy - hMm / 2f;
    }
}
