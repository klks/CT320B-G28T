using CT320B.LabelDesigner.Core.VariableData;

namespace CT320B.LabelDesigner.Core.Model;

/// <summary>
/// A complete label design: its physical size and print parameters plus the ordered set of
/// elements. Sizes are in millimetres; the renderer turns this into a bitmap at any scale and the
/// print job renders it at the printer's dot pitch and sends it via
/// <c>CT320BPrinter.PrintImageLabel</c>.
/// </summary>
public sealed class LabelDocument
{
    /// <summary>Optional document name (used for the title bar / saved-file default).</summary>
    public string Name { get; set; } = "";

    /// <summary>Label width in millimetres.</summary>
    public float WidthMm { get; set; } = 30f;

    /// <summary>Label height in millimetres.</summary>
    public float HeightMm { get; set; } = 40f;

    /// <summary>Gap between labels in millimetres (the TSPL <c>GAP</c> value).</summary>
    public float GapMm { get; set; } = 2f;

    /// <summary>Print speed (TSPL <c>SPEED</c>, 1–14).</summary>
    public int Speed { get; set; } = 5;

    /// <summary>Print density / darkness (TSPL <c>DENSITY</c>, 0–15).</summary>
    public int Density { get; set; } = 8;

    /// <summary>Nominal printer resolution this document targets (informational; the print job
    /// always rasterizes at <see cref="Units.DotsPerMm"/>).</summary>
    public int Dpi { get; set; } = (int)Units.Dpi;

    /// <summary>Print calibration: horizontal shift (mm) applied to all content at print time to
    /// compensate for a printer that lands the image off-origin. Not shown on the editing canvas.</summary>
    public float PrintOffsetXMm { get; set; }

    /// <summary>Print calibration: vertical shift (mm) applied to all content at print time. Negative
    /// moves content up (e.g. -1 fixes content printing ~1 mm too low).</summary>
    public float PrintOffsetYMm { get; set; }

    /// <summary>The elements on the label. Render order is by <see cref="LabelElement.ZOrder"/>,
    /// not list position (see <see cref="ElementsByZOrder"/>).</summary>
    public List<LabelElement> Elements { get; set; } = [];

    /// <summary>Elements ordered back-to-front for rendering (stable within equal ZOrder).</summary>
    public IEnumerable<LabelElement> ElementsByZOrder => Elements.OrderBy(e => e.ZOrder);

    /// <summary>Variable-data serial counters defined on this design. Each contributes a <c>{name}</c>
    /// token used by batch printing (see <see cref="VariableData.BatchExpander"/>). Empty for a plain
    /// (non-variable) label.</summary>
    public List<SerialCounter> Counters { get; set; } = [];
}
