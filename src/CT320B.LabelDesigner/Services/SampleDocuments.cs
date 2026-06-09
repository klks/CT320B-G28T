using System.Drawing;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Model.Elements;

namespace CT320B.LabelDesigner.Services;

/// <summary>Built-in sample documents (placeholder until the template library lands in Phase 8).</summary>
public static class SampleDocuments
{
    /// <summary>A 30×40 mm starter label with a border, title/subtitle, a bar, and an ellipse.</summary>
    public static LabelDocument Starter()
    {
        // This CT320B lands the image ~1 mm low and ~1 mm right (a hardware origin/dead-zone — the
        // native API applies no compensation; it prints at REFERENCE 0,0 with the literal BITMAP
        // coords). Pull content up/left 1 mm by default; adjustable in the Print group.
        var doc = new LabelDocument
        { Name = "Sample", WidthMm = 30, HeightMm = 40, PrintOffsetXMm = -1f, PrintOffsetYMm = -1f };
        doc.Elements.Add(new ShapeElement
        {
            Name = "border", Kind = ShapeKind.RoundRect, StrokeWidthMm = 0.5f, CornerRadiusMm = 2f,
            ZOrder = 0, BoundsMm = new RectangleF(1.5f, 1.5f, 27f, 37f),
        });
        doc.Elements.Add(new TextElement
        {
            Name = "title", Text = "CT320B", FontSizePt = 16, Bold = true,
            Alignment = TextAlignment.Center, Wrap = false, ZOrder = 1,
            BoundsMm = new RectangleF(1.5f, 4, 27, 8),
        });
        doc.Elements.Add(new TextElement
        {
            Name = "subtitle", Text = "Label Designer", FontSizePt = 8,
            Alignment = TextAlignment.Center, Wrap = false, ZOrder = 2,
            BoundsMm = new RectangleF(1.5f, 14, 27, 5),
        });
        doc.Elements.Add(new ShapeElement
        {
            Name = "bar", Kind = ShapeKind.Box, Filled = true, FillColor = Color.Black,
            ZOrder = 3, BoundsMm = new RectangleF(4, 22, 22, 2.5f),
        });
        doc.Elements.Add(new ShapeElement
        {
            Name = "dot", Kind = ShapeKind.Ellipse, Filled = true, FillColor = Color.Black,
            ZOrder = 4, BoundsMm = new RectangleF(12.5f, 28, 5, 5),
        });
        return doc;
    }
}
