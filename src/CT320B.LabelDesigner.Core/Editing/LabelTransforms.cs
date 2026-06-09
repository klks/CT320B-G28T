using System.Drawing;
using CT320B.LabelDesigner.Core.Model;

namespace CT320B.LabelDesigner.Core.Editing;

/// <summary>How to align elements within their shared bounding box.</summary>
public enum AlignKind { Left, HCenter, Right, Top, VMiddle, Bottom }

/// <summary>
/// Pure geometry transforms over a selection, each returning an undoable <see cref="GeometryCommand"/>
/// (or null when the operation needs more elements than given). The caller executes the command on
/// its <see cref="UndoStack"/>. No UI dependency, so these are unit-tested directly.
/// </summary>
public static class LabelTransforms
{
    /// <summary>Aligns elements to an edge/centre of their combined bounding box (needs ≥ 2).</summary>
    public static GeometryCommand? Align(IReadOnlyList<LabelElement> elements, AlignKind kind)
    {
        if (elements is null || elements.Count < 2) return null;
        RectangleF box = BoundingBox(elements);

        return Build($"Align {kind}", elements, b => kind switch
        {
            AlignKind.Left => b with { X = box.Left },
            AlignKind.Right => b with { X = box.Right - b.Width },
            AlignKind.HCenter => b with { X = box.Left + (box.Width - b.Width) / 2f },
            AlignKind.Top => b with { Y = box.Top },
            AlignKind.Bottom => b with { Y = box.Bottom - b.Height },
            AlignKind.VMiddle => b with { Y = box.Top + (box.Height - b.Height) / 2f },
            _ => b,
        });
    }

    /// <summary>Distributes elements so the gaps between them are equal (needs ≥ 3). The first and
    /// last (by position) stay put.</summary>
    public static GeometryCommand? Distribute(IReadOnlyList<LabelElement> elements, bool horizontal)
    {
        if (elements is null || elements.Count < 3) return null;

        List<LabelElement> ordered = horizontal
            ? [.. elements.OrderBy(e => e.XMm)]
            : [.. elements.OrderBy(e => e.YMm)];

        float start = horizontal ? ordered[0].XMm : ordered[0].YMm;
        LabelElement last = ordered[^1];
        float end = horizontal ? last.XMm + last.WidthMm : last.YMm + last.HeightMm;
        float sizes = ordered.Sum(e => horizontal ? e.WidthMm : e.HeightMm);
        float gap = (end - start - sizes) / (ordered.Count - 1);

        var positions = new Dictionary<LabelElement, float>();
        float cursor = start;
        foreach (LabelElement e in ordered)
        {
            positions[e] = cursor;
            cursor += (horizontal ? e.WidthMm : e.HeightMm) + gap;
        }

        return Build(horizontal ? "Distribute horizontally" : "Distribute vertically", elements,
            b => b, // bounds tweak happens via the captured map below
            (el, g) => horizontal
                ? g with { Bounds = g.Bounds with { X = positions[el] } }
                : g with { Bounds = g.Bounds with { Y = positions[el] } });
    }

    /// <summary>Adds <paramref name="degrees"/> to each element's rotation (normalized to [0,360)).</summary>
    public static GeometryCommand? Rotate(IReadOnlyList<LabelElement> elements, float degrees)
    {
        if (elements is null || elements.Count == 0) return null;
        return BuildG("Rotate", elements,
            g => g with { Rotation = Normalize(g.Rotation + degrees) });
    }

    /// <summary>Flips elements horizontally or vertically: toggles the mirror flag and mirrors each
    /// element's position within the selection's bounding box (so a group flips as a whole; a single
    /// element just mirrors its own content).</summary>
    public static GeometryCommand? Flip(IReadOnlyList<LabelElement> elements, bool horizontal)
    {
        if (elements is null || elements.Count == 0) return null;
        RectangleF box = BoundingBox(elements);
        return BuildG(horizontal ? "Flip horizontal" : "Flip vertical", elements, g =>
            horizontal
                ? g with
                {
                    FlipH = !g.FlipH,
                    Bounds = g.Bounds with { X = box.Left + box.Right - g.Bounds.X - g.Bounds.Width },
                }
                : g with
                {
                    FlipV = !g.FlipV,
                    Bounds = g.Bounds with { Y = box.Top + box.Bottom - g.Bounds.Y - g.Bounds.Height },
                });
    }

    /// <summary>The union of the elements' millimetre bounds.</summary>
    public static RectangleF BoundingBox(IReadOnlyList<LabelElement> elements)
    {
        RectangleF box = elements[0].BoundsMm;
        for (int i = 1; i < elements.Count; i++) box = RectangleF.Union(box, elements[i].BoundsMm);
        return box;
    }

    private static float Normalize(float degrees)
    {
        float d = degrees % 360f;
        return d < 0 ? d + 360f : d;
    }

    // Build a command from a per-element bounds transform.
    private static GeometryCommand Build(
        string name, IReadOnlyList<LabelElement> elements, Func<RectangleF, RectangleF> transform)
        => BuildG(name, elements, g => g with { Bounds = transform(g.Bounds) });

    // Build with both a bounds transform (unused arg form) and a per-element geometry transform.
    private static GeometryCommand Build(
        string name, IReadOnlyList<LabelElement> elements,
        Func<RectangleF, RectangleF> _, Func<LabelElement, ElementGeometry, ElementGeometry> perElement)
    {
        ElementGeometry[] before = [.. elements.Select(ElementGeometry.Capture)];
        ElementGeometry[] after = [.. elements.Select((e, i) => perElement(e, before[i]))];
        return new GeometryCommand(name, elements, before, after);
    }

    // Build from a pure geometry transform.
    private static GeometryCommand BuildG(
        string name, IReadOnlyList<LabelElement> elements, Func<ElementGeometry, ElementGeometry> transform)
    {
        ElementGeometry[] before = [.. elements.Select(ElementGeometry.Capture)];
        ElementGeometry[] after = [.. before.Select(transform)];
        return new GeometryCommand(name, elements, before, after);
    }
}
