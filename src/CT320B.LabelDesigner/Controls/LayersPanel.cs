using CT320B.LabelDesigner.Core.Editing;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// Lists the document's elements top-first (highest z-order), with per-row visibility checkboxes and
/// a lock indicator, two-way selection sync with the canvas, and reorder buttons
/// (bring-to-front / forward / backward / send-to-back). Reorders and visibility toggles are
/// recorded on the undo stack.
/// </summary>
public sealed class LayersPanel : UserControl
{
    private readonly CanvasControl _canvas;
    private readonly UndoStack _history;
    private readonly ListView _list = new();
    private bool _syncing;

    public LayersPanel(CanvasControl canvas, UndoStack history)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _history = history ?? throw new ArgumentNullException(nameof(history));

        Padding = new Padding(8);
        BuildLayout();

        _canvas.SelectionChanged += (_, _) => SyncSelectionToList();
        _canvas.DocumentChanged += (_, _) => Populate();
        _history.Changed += (_, _) => Populate();
        Populate();
    }

    private void BuildLayout()
    {
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        // Note: ListView.CheckBoxes is deliberately avoided — it throws ArgumentNullException in
        // WmReflectNotify on this WinForms build. Visibility/lock are toggled via column clicks.
        _list.Columns.Add(Loc.T("Layer"), 132);
        _list.Columns.Add(Loc.T("Vis"), 34);
        _list.Columns.Add(Loc.T("Lock"), 34);
        _list.MouseClick += OnListMouseClick;
        _list.SelectedIndexChanged += OnListSelectionChanged;

        var buttons = new FlowLayoutPanel
        { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 6, 0, 0) };
        AddButton(buttons, Loc.T("Front"), () => Reorder(ReorderKind.Front));
        AddButton(buttons, "▲", () => Reorder(ReorderKind.Forward));
        AddButton(buttons, "▼", () => Reorder(ReorderKind.Backward));
        AddButton(buttons, Loc.T("Back"), () => Reorder(ReorderKind.Back));
        AddButton(buttons, Loc.T("Lock"), ToggleLock);

        Controls.Add(_list);
        Controls.Add(buttons);
    }

    private static void AddButton(FlowLayoutPanel host, string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 4, 0) };
        b.Click += (_, _) => onClick();
        host.Controls.Add(b);
    }

    // Elements front-first (top layer first): highest ZOrder first, list order breaks ties (latest on top).
    private List<LabelElement> DisplayOrder() =>
        [.. _canvas.Document.Elements
            .Select((el, i) => (el, i))
            .OrderByDescending(t => t.el.ZOrder).ThenByDescending(t => t.i)
            .Select(t => t.el)];

    private void Populate()
    {
        _syncing = true;
        try
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (LabelElement el in DisplayOrder())
            {
                var item = new ListViewItem(DisplayName(el)) { Tag = el };
                item.SubItems.Add(el.Visible ? "✓" : "—");
                item.SubItems.Add(el.Locked ? "🔒" : "");
                item.Selected = _canvas.Selection.Contains(el);
                _list.Items.Add(item);
            }
            _list.EndUpdate();
        }
        finally { _syncing = false; }
    }

    private static string DisplayName(LabelElement el)
    {
        string type = el.GetType().Name.Replace("Element", "");
        return string.IsNullOrEmpty(el.Name) ? type : $"{el.Name} ({type})";
    }

    private void SyncSelectionToList()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            foreach (ListViewItem item in _list.Items)
                item.Selected = item.Tag is LabelElement el && _canvas.Selection.Contains(el);
        }
        finally { _syncing = false; }
    }

    private void OnListSelectionChanged(object? sender, EventArgs e)
    {
        if (_syncing) return;
        var selected = _list.SelectedItems.Cast<ListViewItem>()
            .Select(i => i.Tag).OfType<LabelElement>().ToList();
        _syncing = true;
        try { _canvas.SetSelection(selected); }
        finally { _syncing = false; }
    }

    private void OnListMouseClick(object? sender, MouseEventArgs e)
    {
        ListViewHitTestInfo hit = _list.HitTest(e.Location);
        if (hit.Item?.Tag is not LabelElement el) return;
        int col = hit.Item.SubItems.IndexOf(hit.SubItem);   // 0 Layer, 1 Vis, 2 Lock
        if (col == 1) ToggleVisible(el);
        else if (col == 2) ToggleLockOne(el);
    }

    private void ToggleVisible(LabelElement el)
    {
        bool value = !el.Visible;
        _history.Execute(new DelegateCommand(
            value ? "Show layer" : "Hide layer",
            () => el.Visible = value, () => el.Visible = !value));
        _canvas.RefreshDocument();
        Populate();
    }

    private void ToggleLockOne(LabelElement el)
    {
        bool value = !el.Locked;
        _history.Execute(new DelegateCommand(
            value ? "Lock layer" : "Unlock layer",
            () => el.Locked = value, () => el.Locked = !value));
        Populate();
    }

    private void ToggleLock()
    {
        var selected = _canvas.Selection.ToList();
        if (selected.Count == 0) return;
        var before = selected.ToDictionary(el => el, el => el.Locked);
        bool target = !selected.All(el => el.Locked);   // if any unlocked → lock all; else unlock all
        _history.Execute(new DelegateCommand("Toggle lock",
            () => { foreach (LabelElement el in selected) el.Locked = target; },
            () => { foreach (var kv in before) kv.Key.Locked = kv.Value; }));
        Populate();
    }

    private enum ReorderKind { Front, Forward, Backward, Back }

    private void Reorder(ReorderKind kind)
    {
        var sel = _canvas.Selection.ToHashSet();
        if (sel.Count == 0) return;

        List<LabelElement> disp = DisplayOrder();   // front-first
        switch (kind)
        {
            case ReorderKind.Front:
                disp = [.. disp.Where(sel.Contains), .. disp.Where(el => !sel.Contains(el))];
                break;
            case ReorderKind.Back:
                disp = [.. disp.Where(el => !sel.Contains(el)), .. disp.Where(sel.Contains)];
                break;
            case ReorderKind.Forward:   // toward front = toward index 0
                for (int i = 1; i < disp.Count; i++)
                    if (sel.Contains(disp[i]) && !sel.Contains(disp[i - 1]))
                        (disp[i - 1], disp[i]) = (disp[i], disp[i - 1]);
                break;
            case ReorderKind.Backward:
                for (int i = disp.Count - 2; i >= 0; i--)
                    if (sel.Contains(disp[i]) && !sel.Contains(disp[i + 1]))
                        (disp[i + 1], disp[i]) = (disp[i], disp[i + 1]);
                break;
        }
        ApplyOrder(disp);
    }

    // Writes ZOrder from a front-first display list (index 0 → highest z), as one undoable command.
    private void ApplyOrder(List<LabelElement> displayFrontFirst)
    {
        int n = displayFrontFirst.Count;
        var oldZ = _canvas.Document.Elements.ToDictionary(el => el, el => el.ZOrder);
        var newZ = new Dictionary<LabelElement, int>();
        for (int i = 0; i < n; i++) newZ[displayFrontFirst[i]] = n - 1 - i;
        if (oldZ.All(kv => newZ.TryGetValue(kv.Key, out int z) && z == kv.Value)) return;   // no change

        _history.Execute(new DelegateCommand("Reorder layers",
            () => { foreach (var kv in newZ) kv.Key.ZOrder = kv.Value; },
            () => { foreach (var kv in oldZ) kv.Key.ZOrder = kv.Value; }));
        _canvas.RefreshDocument();
        Populate();
    }
}
