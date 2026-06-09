using System.ComponentModel;
using CT320B.LabelDesigner.Core.Editing;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// One open label = one document tab: a self-contained editor that owns its <see cref="CanvasControl"/>,
/// <see cref="UndoStack"/>, and Properties/Layers inspector, plus its file path and dirty state. Because
/// each tab has its own canvas + panels, switching tabs needs no rebinding — the shared ribbon/insert
/// bar simply act on the active editor. Raises <see cref="Changed"/> whenever something the shell cares
/// about updates (history, document, zoom, dirty, file path).
/// </summary>
public sealed class LabelEditor : UserControl
{
    /// <summary>Stable per-tab id used to name this tab's crash-recovery snapshot (Phase 14b).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RecoveryId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The undo/redo stack for this document.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UndoStack History { get; } = new();

    /// <summary>The editing surface for this document.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CanvasControl Canvas { get; } = new();

    private string? _filePath;
    private bool _dirty;
    private bool _loading;
    private PropertiesPanel _properties = null!;

    /// <summary>The measurement unit for the canvas rulers + properties length fields (Phase 14d).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Core.Model.MeasurementUnit Unit
    {
        set { Canvas.Unit = value; _properties.Unit = value; }
    }

    /// <summary>The backing file (a <c>.ct320b.json</c>), or null for a never-saved document.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? FilePath
    {
        get => _filePath;
        set { _filePath = value; Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>True when there are unsaved changes.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dirty => _dirty;

    /// <summary>The document being edited.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LabelDocument Document => Canvas.Document;

    /// <summary>Display name for the tab/title: the file name (sans extension), else the document name.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get
        {
            if (_filePath is { } path)
            {
                string file = Path.GetFileName(path);
                return file.EndsWith(AppPaths.Extension, StringComparison.OrdinalIgnoreCase)
                    ? file[..^AppPaths.Extension.Length]
                    : Path.GetFileNameWithoutExtension(file);
            }
            return string.IsNullOrWhiteSpace(Document.Name) ? "Untitled" : Document.Name;
        }
    }

    /// <summary>Fires on any state change the shell reflects (history, document, zoom, dirty, file path).</summary>
    public event EventHandler? Changed;

    public LabelEditor(LabelDocument document, string? filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        Dock = DockStyle.Fill;

        Canvas.Dock = DockStyle.Fill;
        Canvas.History = History;
        _loading = true;
        Canvas.Document = document;
        _filePath = filePath;
        _loading = false;

        var center = new Panel { Dock = DockStyle.Fill };
        center.Controls.Add(Canvas);

        Controls.Add(center);
        Controls.Add(new Splitter { Dock = DockStyle.Right, Width = 4 });
        Controls.Add(BuildInspectorPane());

        History.Changed += (_, _) => { Canvas.SyncSelection(); MarkDirty(); Raise(); };
        Canvas.DocumentChanged += (_, _) => { MarkDirty(); Raise(); };
        Canvas.ZoomChanged += (_, _) => Raise();
    }

    /// <summary>Clears history and dirty state after a load/save so the document reads as unmodified.</summary>
    public void MarkSaved()
    {
        _dirty = false;
        Raise();
    }

    /// <summary>Flags the document modified (for changes that don't go through the canvas, e.g. label
    /// setup or print-offset edits) and notifies the shell.</summary>
    public void MarkModified()
    {
        MarkDirty();
        Raise();
    }

    private void MarkDirty()
    {
        if (_loading || _dirty) return;
        _dirty = true;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    // Right inspector: Properties (top) + Layers (fill) — one set per editor (no rebinding).
    private Control BuildInspectorPane()
    {
        var properties = new PropertiesPanel(Canvas, History) { Dock = DockStyle.Fill };
        _properties = properties;
        var layers = new LayersPanel(Canvas, History) { Dock = DockStyle.Fill };

        var layersHost = new Panel { Dock = DockStyle.Fill };
        layersHost.Controls.Add(layers);
        layersHost.Controls.Add(Header(Services.Loc.T("LayersTitle")));

        var propsHost = new Panel { Dock = DockStyle.Top, Height = 360 };
        propsHost.Controls.Add(properties);
        propsHost.Controls.Add(Header(Services.Loc.T("PropertiesTitle")));

        var pane = new Panel { Dock = DockStyle.Right, Width = 260 };
        pane.Controls.Add(layersHost);
        // A clearly visible (still draggable) divider between Properties and Layers.
        pane.Controls.Add(new Splitter
        {
            Dock = DockStyle.Top, Height = 5, BackColor = Color.FromArgb(158, 158, 164),
            MinExtra = 80, MinSize = 80,
        });
        pane.Controls.Add(propsHost);
        return pane;
    }

    private static Label Header(string text) => new()
    {
        Text = text, Dock = DockStyle.Top, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
        Padding = new Padding(4, 3, 0, 3), BackColor = Color.FromArgb(238, 238, 240),
    };
}
