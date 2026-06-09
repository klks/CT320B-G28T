using CT320B.LabelDesigner.Core.Model;

namespace CT320B.LabelDesigner.Core.Rendering;

/// <summary>
/// Per-render settings shared by every element: the millimetre-to-pixel scale and whether to
/// antialias. The <b>same</b> element <c>Render</c> code runs for both the on-screen canvas
/// (antialiased, at screen scale) and the print job (no antialias, at exactly the printer's dot
/// pitch), so what you see is what prints.
/// </summary>
public sealed class RenderContext
{
    /// <summary>Pixels (device dots) per millimetre for this render.</summary>
    public double PixelsPerMm { get; }

    /// <summary>Whether geometry/text should be antialiased (off for the 1-bpp print render).</summary>
    public bool AntiAlias { get; }

    /// <summary>The effective rendering resolution in dpi (derived from <see cref="PixelsPerMm"/>).</summary>
    public double Dpi => PixelsPerMm * Units.MmPerInch;

    public RenderContext(double pixelsPerMm, bool antiAlias)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerMm);
        PixelsPerMm = pixelsPerMm;
        AntiAlias = antiAlias;
    }

    /// <summary>Converts a millimetre measurement to device pixels for this render.</summary>
    public float MmToPx(double mm) => (float)(mm * PixelsPerMm);

    /// <summary>The print context: exactly <see cref="Units.DotsPerMm"/> px/mm, antialiasing off,
    /// so the rendered bitmap is <c>WidthMm*8 × HeightMm*8</c> device dots.</summary>
    public static RenderContext ForPrint() => new(Units.DotsPerMm, antiAlias: false);

    /// <summary>A screen context at the given px/mm scale, antialiased for legible editing.</summary>
    public static RenderContext ForScreen(double pixelsPerMm) => new(pixelsPerMm, antiAlias: true);
}
