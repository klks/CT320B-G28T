using System.Drawing;
using CT320B.LabelDesigner.Core.Model;

namespace CT320B.LabelDesigner.Core.Editing;

/// <summary>An element's reversible geometric state: position/size, rotation, and mirror flags.</summary>
public readonly record struct ElementGeometry(RectangleF Bounds, float Rotation, bool FlipH, bool FlipV)
{
    /// <summary>Snapshots an element's current geometry.</summary>
    public static ElementGeometry Capture(LabelElement e) =>
        new(e.BoundsMm, e.Rotation, e.FlipH, e.FlipV);

    /// <summary>Writes this geometry onto an element.</summary>
    public void ApplyTo(LabelElement e)
    {
        e.BoundsMm = Bounds;
        e.Rotation = Rotation;
        e.FlipH = FlipH;
        e.FlipV = FlipV;
    }
}

/// <summary>
/// Sets the geometry (bounds + rotation + flips) of one or more elements, reversibly. Covers
/// drag-move, resize, nudge, align, distribute, rotate, and flip — every transform reduces to a
/// before/after geometry snapshot per element.
/// </summary>
public sealed class GeometryCommand : IUndoableCommand
{
    private readonly LabelElement[] _elements;
    private readonly ElementGeometry[] _before;
    private readonly ElementGeometry[] _after;

    public string Name { get; }

    public GeometryCommand(
        string name, IReadOnlyList<LabelElement> elements,
        IReadOnlyList<ElementGeometry> before, IReadOnlyList<ElementGeometry> after)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (elements.Count != before.Count || elements.Count != after.Count)
            throw new ArgumentException("elements, before and after must be the same length.");
        Name = name;
        _elements = [.. elements];
        _before = [.. before];
        _after = [.. after];
    }

    /// <summary>Builds a command that moves every element by (<paramref name="dxMm"/>,
    /// <paramref name="dyMm"/>), snapshotting current geometry as the "before".</summary>
    public static GeometryCommand Move(
        string name, IReadOnlyList<LabelElement> elements, float dxMm, float dyMm)
    {
        ElementGeometry[] before = [.. elements.Select(ElementGeometry.Capture)];
        ElementGeometry[] after = [.. before.Select(g =>
            g with { Bounds = g.Bounds with { X = g.Bounds.X + dxMm, Y = g.Bounds.Y + dyMm } })];
        return new GeometryCommand(name, elements, before, after);
    }

    public void Do() => Apply(_after);
    public void Undo() => Apply(_before);

    private void Apply(ElementGeometry[] state)
    {
        for (int i = 0; i < _elements.Length; i++) state[i].ApplyTo(_elements[i]);
    }
}
