using System.ComponentModel;
using CT320B.LabelDesigner.Core.Model;

namespace CT320B.LabelDesigner.Controls;

/// <summary>Cancelable notification that a document tab is about to close (for an unsaved-changes prompt).</summary>
public sealed class EditorClosingEventArgs(LabelEditor editor) : EventArgs
{
    public LabelEditor Editor { get; } = editor;
    public bool Cancel { get; set; }
}

/// <summary>
/// A multi-document tab strip: a row of closable headers (with a dirty <c>*</c> marker) above a content
/// area that shows the active <see cref="LabelEditor"/>. Lets several labels be edited at once; the shell
/// reflects the <see cref="ActiveEditor"/> and listens to <see cref="ActiveChanged"/>.
/// </summary>
public sealed class EditorTabs : UserControl
{
    private readonly FlowLayoutPanel _strip = new()
    {
        Dock = DockStyle.Top, Height = 30, WrapContents = false, AutoScroll = true,
        FlowDirection = FlowDirection.LeftToRight, BackColor = Color.FromArgb(228, 228, 232),
        Padding = new Padding(4, 4, 0, 0),
    };
    private readonly Panel _content = new() { Dock = DockStyle.Fill };
    private readonly List<LabelEditor> _editors = [];
    private readonly Dictionary<LabelEditor, TabHead> _heads = [];
    private LabelEditor? _active;

    /// <summary>The editor of the selected tab, or null when none are open.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LabelEditor? ActiveEditor => _active;

    /// <summary>All open editors, in tab order.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<LabelEditor> Editors => _editors;

    /// <summary>Raised when the active editor changes or its state (title/dirty/zoom/history) updates.</summary>
    public event EventHandler? ActiveChanged;

    /// <summary>Raised (cancelable) before a tab closes, so the shell can prompt to save.</summary>
    public event EventHandler<EditorClosingEventArgs>? EditorClosing;

    public EditorTabs()
    {
        Controls.Add(_content);
        Controls.Add(_strip);
    }

    /// <summary>Opens <paramref name="document"/> in a new tab and activates it.</summary>
    public LabelEditor AddEditor(LabelDocument document, string? filePath)
    {
        var editor = new LabelEditor(document, filePath) { Visible = false };
        _editors.Add(editor);
        _content.Controls.Add(editor);

        var head = new TabHead(editor);
        head.Activated += (_, _) => Activate(editor);
        head.Closed += (_, _) => CloseEditor(editor);
        _heads[editor] = head;
        _strip.Controls.Add(head);

        editor.Changed += OnEditorChanged;
        Activate(editor);
        return editor;
    }

    /// <summary>Selects an open editor's tab.</summary>
    public void Activate(LabelEditor editor)
    {
        if (!_editors.Contains(editor)) return;
        _active = editor;
        foreach (LabelEditor e in _editors) e.Visible = e == editor;
        editor.BringToFront();
        foreach (TabHead h in _heads.Values) h.SetActive(h.Editor == editor);
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes a tab (after the cancelable <see cref="EditorClosing"/>); returns false if cancelled.</summary>
    public bool CloseEditor(LabelEditor editor)
    {
        var args = new EditorClosingEventArgs(editor);
        EditorClosing?.Invoke(this, args);
        if (args.Cancel) return false;

        int index = _editors.IndexOf(editor);
        editor.Changed -= OnEditorChanged;
        _editors.Remove(editor);
        _strip.Controls.Remove(_heads[editor]);
        _heads[editor].Dispose();
        _heads.Remove(editor);
        _content.Controls.Remove(editor);
        editor.Dispose();

        if (_active == editor)
        {
            _active = null;
            if (_editors.Count > 0)
                Activate(_editors[Math.Min(index, _editors.Count - 1)]);
            else
                ActiveChanged?.Invoke(this, EventArgs.Empty);
        }
        return true;
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (sender is LabelEditor ed && _heads.TryGetValue(ed, out TabHead? head)) head.UpdateText();
        if (sender == _active) ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A single tab header: title (+ dirty <c>*</c>) and a close button.</summary>
    private sealed class TabHead : FlowLayoutPanel
    {
        public LabelEditor Editor { get; }
        private readonly Label _title = new() { AutoSize = true, Margin = new Padding(0, 4, 6, 0) };
        private readonly Label _close = new()
        {
            AutoSize = true, Text = "✕", Margin = new Padding(0, 4, 0, 0),
            ForeColor = Color.Gray, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 8f),
        };
        private bool _isActive;

        public event EventHandler? Activated;
        public event EventHandler? Closed;

        public TabHead(LabelEditor editor)
        {
            Editor = editor;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = false;
            Margin = new Padding(0, 0, 2, 0);
            Padding = new Padding(10, 3, 8, 3);

            Controls.Add(_title);
            Controls.Add(_close);

            Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
            _title.Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
            _close.Click += (_, _) => Closed?.Invoke(this, EventArgs.Empty);

            Render();
        }

        public void SetActive(bool active) { _isActive = active; Render(); }

        public void UpdateText() => Render();

        private void Render()
        {
            _title.Text = Editor.Title + (Editor.Dirty ? " *" : "");
            BackColor = _isActive ? Color.White : Color.FromArgb(214, 214, 220);
            _title.Font = new Font("Segoe UI", 9f, _isActive ? FontStyle.Bold : FontStyle.Regular);
        }
    }
}
