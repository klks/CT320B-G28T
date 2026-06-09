using CT320B.LabelDesigner.Core.Model;

namespace CT320B.LabelDesigner.Core.Editing;

/// <summary>Adds one or more elements to a document, reversibly.</summary>
public sealed class AddElementsCommand : IUndoableCommand
{
    private readonly LabelDocument _doc;
    private readonly LabelElement[] _elements;

    public string Name { get; }

    public AddElementsCommand(LabelDocument doc, IReadOnlyList<LabelElement> elements, string? name = null)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        ArgumentNullException.ThrowIfNull(elements);
        _elements = [.. elements];
        Name = name ?? (_elements.Length == 1 ? "Add element" : $"Add {_elements.Length} elements");
    }

    public void Do() => _doc.Elements.AddRange(_elements);

    public void Undo()
    {
        foreach (LabelElement e in _elements) _doc.Elements.Remove(e);
    }
}

/// <summary>Removes one or more elements from a document, reversibly (restores their list positions).</summary>
public sealed class RemoveElementsCommand : IUndoableCommand
{
    private readonly LabelDocument _doc;
    private readonly (LabelElement element, int index)[] _removed;

    public string Name { get; }

    public RemoveElementsCommand(LabelDocument doc, IReadOnlyList<LabelElement> elements, string? name = null)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        ArgumentNullException.ThrowIfNull(elements);
        // Capture each element's current index so Undo re-inserts it in the same place.
        _removed = [.. elements
            .Select(e => (element: e, index: doc.Elements.IndexOf(e)))
            .Where(t => t.index >= 0)
            .OrderBy(t => t.index)];
        int n = _removed.Length;
        Name = name ?? (n == 1 ? "Delete element" : $"Delete {n} elements");
    }

    public void Do()
    {
        // Remove from highest index down so earlier indices stay valid.
        foreach ((LabelElement element, _) in _removed.OrderByDescending(t => t.index))
            _doc.Elements.Remove(element);
    }

    public void Undo()
    {
        // Re-insert from lowest index up to restore original positions.
        foreach ((LabelElement element, int index) in _removed)
            _doc.Elements.Insert(Math.Min(index, _doc.Elements.Count), element);
    }
}
