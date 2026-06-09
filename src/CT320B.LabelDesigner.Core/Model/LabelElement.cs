using System.Drawing;
using System.Text.Json.Serialization;
using CT320B.LabelDesigner.Core.Model.Elements;
using CT320B.LabelDesigner.Core.Rendering;

namespace CT320B.LabelDesigner.Core.Model;

/// <summary>
/// Base class for everything placed on a label. Geometry is stored in <b>millimetres</b> as four
/// scalars (so it serializes cleanly and stays resolution-independent); <see cref="BoundsMm"/> is a
/// convenience view. Each element knows how to draw itself onto a <see cref="Graphics"/> via
/// <see cref="Render"/>, using the <see cref="RenderContext"/> for the mm→px scale.
///
/// Polymorphic JSON is driven by the <c>"type"</c> discriminator; add a <see cref="JsonDerivedType"/>
/// line here for each new element type.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ShapeElement), "shape")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(QrElement), "qr")]
[JsonDerivedType(typeof(BarcodeElement), "barcode")]
[JsonDerivedType(typeof(TableElement), "table")]
public abstract class LabelElement
{
    /// <summary>Author-facing name (shown in the layers panel). Optional.</summary>
    public string Name { get; set; } = "";

    /// <summary>Left edge in millimetres from the label's top-left origin.</summary>
    public float XMm { get; set; }

    /// <summary>Top edge in millimetres from the label's top-left origin.</summary>
    public float YMm { get; set; }

    /// <summary>Width in millimetres.</summary>
    public float WidthMm { get; set; }

    /// <summary>Height in millimetres.</summary>
    public float HeightMm { get; set; }

    /// <summary>Clockwise rotation in degrees, applied about the element's centre.</summary>
    public float Rotation { get; set; }

    /// <summary>Mirror horizontally (flip about the vertical centre line).</summary>
    public bool FlipH { get; set; }

    /// <summary>Mirror vertically (flip about the horizontal centre line).</summary>
    public bool FlipV { get; set; }

    /// <summary>Stacking order; lower values render first (further back). Equal values keep
    /// insertion order.</summary>
    public int ZOrder { get; set; }

    /// <summary>When false the element is skipped while rendering.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>When true the element is protected from canvas edits (UI hint; ignored by rendering).</summary>
    public bool Locked { get; set; }

    /// <summary>Group tag: elements sharing a non-null id are selected/moved together. Null = ungrouped.
    /// Purely an editing aid (ignored by rendering and printing).</summary>
    public string? GroupId { get; set; }

    /// <summary>When false the element is shown on the editing canvas but excluded from the printed
    /// output — e.g. artwork that is already pre-printed on the label stock. Default true.</summary>
    public bool Printable { get; set; } = true;

    /// <summary>The element bounds as a millimetre rectangle (derived from the X/Y/W/H scalars).</summary>
    [JsonIgnore]
    public RectangleF BoundsMm
    {
        get => new(XMm, YMm, WidthMm, HeightMm);
        set { XMm = value.X; YMm = value.Y; WidthMm = value.Width; HeightMm = value.Height; }
    }

    /// <summary>Draws this element onto <paramref name="g"/>. The caller has already applied any
    /// rotation transform; implementations draw in the (unrotated) pixel space, converting their
    /// millimetre geometry with <paramref name="ctx"/>.</summary>
    public abstract void Render(Graphics g, RenderContext ctx);

    /// <summary>Applies variable-data binding to this element's text-bearing content by passing each
    /// template string through <paramref name="resolve"/> (which substitutes <c>{token}</c> placeholders
    /// for a single batch row). The default is a no-op; text/barcode/QR/table elements override it.
    /// Called on a cloned document, so mutating in place is safe.</summary>
    public virtual void ApplyDataBinding(Func<string, string> resolve) { }
}
