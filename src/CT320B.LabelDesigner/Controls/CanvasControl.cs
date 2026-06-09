using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Text.Json;
using CT320B.LabelDesigner.Core.Editing;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Model.Elements;
using CT320B.LabelDesigner.Core.Rendering;
using CT320B.LabelDesigner.Core.Serialization;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// The WYSIWYG editing surface: paints a <see cref="LabelDocument"/> scaled (gray desk, white page
/// with shadow + border, mm grid, top/left rulers) and supports zoom/pan, click + marquee selection,
/// 8-handle resize, drag-move with snap-to-grid, and keyboard nudge/delete. Elements are drawn with
/// the shared <see cref="LabelRenderer"/> (on a transparent layer over the page) so the canvas shows
/// exactly what will print. Undo/redo + clipboard arrive with the Phase 4 command stack.
/// </summary>
public sealed class CanvasControl : Control
{
    private const int RulerSize = 22;     // px gutter for the rulers
    private const int HandleSize = 8;     // px square selection handles
    private const float RotateHandleOffsetPx = 22f;   // knob distance above the top-centre handle
    private const float RotateHandleRadiusPx = 5f;    // knob radius / hit slack
    private const float HitTolerancePx = 3f;   // click slack for picking thin strokes / "rendered" pixels
    private const float MinElementMm = 1f;
    private const float MinZoom = 0.5f, MaxZoom = 80f;

    // Handle anchor factors (fraction of width/height), clockwise from top-left.
    private static readonly (float fx, float fy)[] HandleFactors =
        [(0, 0), (0.5f, 0), (1, 0), (1, 0.5f), (1, 1), (0.5f, 1), (0, 1), (0, 0.5f)];

    private LabelDocument _document = new();
    private float _zoom = (float)Units.DotsPerMm;   // px per mm (100% = 1 printer dot : 1 screen px)
    private PointF _pan = new(RulerSize + 14, RulerSize + 14);
    private bool _userAdjusted;   // true once the user zooms/pans → stop auto-fitting on resize
    private int _viewRotation;    // 0/90/180/270 — view-only spin (never affects the document or print)

    private readonly List<LabelElement> _selection = [];
    // Shared across all canvases (every document tab) so copy/cut in one tab can paste into another.
    private static readonly List<LabelElement> _clipboard = [];
    private readonly Font _rulerFont = new("Segoe UI", 6.5f);
    private const float PasteOffsetMm = 2f;

    private TextBox? _textEditor;
    private TextElement? _editingText;
    private string _editStartText = "";

    private enum DragMode { None, Move, Resize, Marquee, Pan, Rotate }
    private DragMode _drag = DragMode.None;
    private Point _dragStartScreen;
    private PointF _dragStartMm;
    private int _resizeHandle = -1;
    private readonly Dictionary<LabelElement, RectangleF> _dragOrig = [];
    private PointF _rotateCenter;            // selection centre (screen) — fixed during a rotation drag
    private float _rotateStartPointerAngle;  // pointer angle at drag start (deg)
    private float _rotateStartRotation;      // element rotation at drag start (deg), for undo
    private PointF _panOrigin;
    private bool _panFromDesk;   // a left-drag pan started on the gray desk (click without drag deselects)
    private Rectangle _marquee;

    /// <summary>Grid spacing in millimetres (also the snap step).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float GridStepMm { get; set; } = 5f;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowGrid { get; set; }            // off by default
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool SnapToGrid { get; set; }          // off by default

    /// <summary>When true, dragging snaps to other elements' edges/centres + the label centre, showing
    /// transient alignment guides. Hold Alt while dragging to bypass. On by default.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowSmartGuides { get; set; } = true;
    private const float GuideTolerancePx = 6f;
    private readonly List<GuideLine> _guides = [];
    private readonly List<SpacingSpan> _spacingSpans = [];   // equal-spacing indicators while dragging

    /// <summary>Inset (mm) of the red "safe area" guide from the label edges — content outside it
    /// risks being clipped by the printer's ~1 mm edge dead-zone. Guide is on-screen only (not printed).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float SafeMarginMm { get; set; } = 1f;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowSafeMargin { get; set; } = true;

    /// <summary>Measurement unit the rulers display (Phase 14d). The model is always millimetres.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MeasurementUnit Unit
    {
        get => _unit;
        set { if (_unit == value) return; _unit = value; Invalidate(); }
    }
    private MeasurementUnit _unit = MeasurementUnit.Millimeters;

    /// <summary>Raised when the selection set changes.</summary>
    public event EventHandler? SelectionChanged;
    /// <summary>Raised when the document is mutated (move/resize/nudge/delete).</summary>
    public event EventHandler? DocumentChanged;
    /// <summary>Raised when the zoom level changes.</summary>
    public event EventHandler? ZoomChanged;

    public CanvasControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable
                 | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
        TabStop = true;
        BackColor = Color.FromArgb(118, 118, 122);
    }

    /// <summary>The document being edited. Setting it clears the selection and fits to view.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LabelDocument Document
    {
        get => _document;
        set
        {
            _document = value ?? new LabelDocument();
            _selection.Clear();
            _userAdjusted = false;
            OnDocumentChanged();   // repopulate layers etc. for the new document
            OnSelectionChanged();
            ZoomToFit();   // no-op until sized; OnSizeChanged refits once the control has a size
            Invalidate();
        }
    }

    /// <summary>The undo/redo stack edits are recorded on (move/resize/nudge/delete). When null,
    /// edits apply directly without history.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UndoStack? History { get; set; }

    /// <summary>Current zoom in screen pixels per millimetre.</summary>
    public float Zoom => _zoom;

    /// <summary>View-only canvas spin in degrees (0/90/180/270). Rotates how the label is displayed
    /// and edited on screen; it never changes the document, the saved file, or the printed output.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ViewRotation
    {
        get => _viewRotation;
        set
        {
            int v = ((value % 360) + 360) % 360;
            v = v / 90 * 90;   // snap to quarter turns
            if (v == _viewRotation) return;
            _viewRotation = v;
            if (!_userAdjusted) ZoomToFit(); else Invalidate();
            ViewRotationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Adds <paramref name="deltaDeg"/> (typically 90) to the view spin, wrapping at 360.</summary>
    public void RotateView(int deltaDeg = 90) => ViewRotation = _viewRotation + deltaDeg;

    /// <summary>Raised when <see cref="ViewRotation"/> changes.</summary>
    public event EventHandler? ViewRotationChanged;

    private bool QuarterTurned => _viewRotation % 180 != 0;

    /// <summary>The currently selected elements.</summary>
    public IReadOnlyList<LabelElement> Selection => _selection;

    /// <summary>Repaints after an external edit (e.g. the properties panel changed an element).</summary>
    public void RefreshDocument() => Invalidate();

    /// <summary>Replaces the selection with the given elements (those present in the document).</summary>
    public void SetSelection(IEnumerable<LabelElement> elements)
    {
        _selection.Clear();
        foreach (LabelElement el in elements)
            if (_document.Elements.Contains(el) && !_selection.Contains(el))
                _selection.Add(el);
        OnSelectionChanged();
        Invalidate();
    }

    /// <summary>True when the clipboard holds elements that can be pasted.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CanPaste => _clipboard.Count > 0;

    /// <summary>Selects every element in the document.</summary>
    public void SelectAll() => SetSelection(_document.Elements);

    /// <summary>Adds a new element, centered on the label and on top, then selects it (reversibly).</summary>
    public void AddElement(LabelElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.ZOrder = _document.Elements.Count == 0 ? 0 : _document.Elements.Max(e => e.ZOrder) + 1;
        element.XMm = Math.Max(0f, (_document.WidthMm - element.WidthMm) / 2f);
        element.YMm = Math.Max(0f, (_document.HeightMm - element.HeightMm) / 2f);
        if (History is not null)
            History.Execute(new AddElementsCommand(_document, [element]));
        else
            _document.Elements.Add(element);
        SetSelection([element]);
        OnDocumentChanged();
        Invalidate();
    }

    /// <summary>Deletes the (unlocked) selected elements via the command stack.</summary>
    public void DeleteSelection()
    {
        List<LabelElement> toRemove = [.. _selection.Where(el => !el.Locked)];   // locked can't be deleted
        if (toRemove.Count == 0) return;
        if (History is not null)
            History.Execute(new RemoveElementsCommand(_document, toRemove));
        else
            foreach (LabelElement el in toRemove) _document.Elements.Remove(el);
        _selection.RemoveAll(toRemove.Contains);
        OnSelectionChanged();
        OnDocumentChanged();
        Invalidate();
    }

    /// <summary>Copies the current selection to the in-app clipboard (deep clones).</summary>
    public void CopySelection()
    {
        _clipboard.Clear();
        _clipboard.AddRange(_selection.Select(Clone));
    }

    /// <summary>Copies then deletes the current selection.</summary>
    public void CutSelection()
    {
        if (_selection.Count == 0) return;
        CopySelection();
        DeleteSelection();
    }

    /// <summary>Pastes the clipboard contents (offset slightly) and selects them.</summary>
    public void Paste() => AddClones(_clipboard, "Paste");

    /// <summary>Duplicates the current selection in place (offset slightly) and selects the copies.</summary>
    public void DuplicateSelection() => AddClones(_selection, "Duplicate");

    private void AddClones(IReadOnlyList<LabelElement> source, string name)
    {
        if (source.Count == 0) return;
        List<LabelElement> clones = [.. source.Select(Clone)];
        foreach (LabelElement c in clones) { c.XMm += PasteOffsetMm; c.YMm += PasteOffsetMm; }
        if (History is not null)
            History.Execute(new AddElementsCommand(_document, clones, name));
        else
            _document.Elements.AddRange(clones);
        SetSelection(clones);
        OnDocumentChanged();
        Invalidate();
    }

    /// <summary>Raises the selected elements above all others (z-order), reversibly.</summary>
    public void BringSelectionToFront() => Restack(toFront: true);

    /// <summary>Lowers the selected elements below all others (z-order), reversibly.</summary>
    public void SendSelectionToBack() => Restack(toFront: false);

    private void Restack(bool toFront)
    {
        if (_selection.Count == 0 || _document.Elements.Count == 0) return;
        var oldZ = _document.Elements.ToDictionary(el => el, el => el.ZOrder);
        var newZ = new Dictionary<LabelElement, int>(oldZ);
        List<LabelElement> sel = [.. _selection.OrderBy(el => el.ZOrder)];
        int z = toFront ? _document.Elements.Max(el => el.ZOrder) + 1
                        : _document.Elements.Min(el => el.ZOrder) - sel.Count;
        foreach (LabelElement el in sel) newZ[el] = z++;
        if (oldZ.All(kv => newZ[kv.Key] == kv.Value)) return;

        RunCommand(new DelegateCommand(toFront ? "Bring to front" : "Send to back",
            () => { foreach (var kv in newZ) kv.Key.ZOrder = kv.Value; },
            () => { foreach (var kv in oldZ) kv.Key.ZOrder = kv.Value; }));
    }

    /// <summary>Toggles the lock state of the selection (locks all if any are unlocked), reversibly.</summary>
    public void ToggleLockSelection()
    {
        List<LabelElement> sel = [.. _selection];
        if (sel.Count == 0) return;
        var before = sel.ToDictionary(el => el, el => el.Locked);
        bool target = !sel.All(el => el.Locked);
        RunCommand(new DelegateCommand("Toggle lock",
            () => { foreach (LabelElement el in sel) el.Locked = target; },
            () => { foreach (var kv in before) kv.Key.Locked = kv.Value; }));
    }

    /// <summary>Tags the selected elements with a shared group id so they select/move together.
    /// No-op for fewer than two elements.</summary>
    public void GroupSelection()
    {
        List<LabelElement> sel = [.. _selection];
        if (sel.Count < 2) return;
        var before = sel.ToDictionary(el => el, el => el.GroupId);
        string gid = Guid.NewGuid().ToString("N");
        RunCommand(new DelegateCommand("Group",
            () => { foreach (LabelElement el in sel) el.GroupId = gid; },
            () => { foreach (var kv in before) kv.Key.GroupId = kv.Value; }));
    }

    /// <summary>Clears the group tag from any selected grouped elements.</summary>
    public void UngroupSelection()
    {
        List<LabelElement> sel = [.. _selection.Where(el => !string.IsNullOrEmpty(el.GroupId))];
        if (sel.Count == 0) return;
        var before = sel.ToDictionary(el => el, el => el.GroupId);
        RunCommand(new DelegateCommand("Ungroup",
            () => { foreach (LabelElement el in sel) el.GroupId = null; },
            () => { foreach (var kv in before) kv.Key.GroupId = kv.Value; }));
    }

    /// <summary>True when the selection contains at least one grouped element (enables Ungroup).</summary>
    public bool SelectionHasGroup => _selection.Any(el => !string.IsNullOrEmpty(el.GroupId));

    // Extends the selection so any element sharing a selected element's group is included too.
    private void ExpandSelectionToGroups()
    {
        var ids = _selection
            .Select(el => el.GroupId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToHashSet();
        if (ids.Count == 0) return;
        foreach (LabelElement el in _document.Elements)
            if (el.GroupId is { } gid && ids.Contains(gid) && !_selection.Contains(el))
                _selection.Add(el);
    }

    private void RunCommand(IUndoableCommand command)
    {
        if (History is not null) History.Execute(command);
        else command.Do();
        OnDocumentChanged();
        Invalidate();
    }

    // Deep-clone an element via the polymorphic JSON serializer (round-trips the concrete type).
    private static LabelElement Clone(LabelElement element) =>
        JsonSerializer.Deserialize<LabelElement>(
            JsonSerializer.Serialize(element, LabelJson.Options), LabelJson.Options)!;

    /// <summary>Drops any selected elements that are no longer in the document (after undo/redo) and
    /// repaints. Call after the history changes.</summary>
    public void SyncSelection()
    {
        int removed = _selection.RemoveAll(el => !_document.Elements.Contains(el));
        if (removed > 0) OnSelectionChanged();
        Invalidate();
    }

    // --- coordinate transforms ---
    private PointF ScreenToMm(float x, float y) => new((x - _pan.X) / _zoom, (y - _pan.Y) / _zoom);

    /// <summary>The label page's centre in (unrotated) screen coordinates — the pivot for the view spin.</summary>
    private PointF PageCenterScreen() =>
        new(_pan.X + _document.WidthMm * _zoom / 2f, _pan.Y + _document.HeightMm * _zoom / 2f);

    /// <summary>Maps a raw (rotated-view) screen point back to the unrotated screen space the rest of
    /// the control reasons in, so editing keeps working while the canvas is spun.</summary>
    private PointF Unrotate(PointF p)
    {
        if (_viewRotation == 0) return p;
        PointF c = PageCenterScreen();
        double rad = -_viewRotation * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        float dx = p.X - c.X, dy = p.Y - c.Y;
        return new PointF(c.X + (float)(dx * cos - dy * sin), c.Y + (float)(dx * sin + dy * cos));
    }

    /// <summary>Returns the mouse args with its location un-rotated into unrotated screen space.</summary>
    private MouseEventArgs Rotated(MouseEventArgs e)
    {
        if (_viewRotation == 0) return e;
        PointF p = Unrotate(e.Location);
        return new MouseEventArgs(e.Button, e.Clicks, (int)Math.Round(p.X), (int)Math.Round(p.Y), e.Delta);
    }
    private RectangleF MmRectToScreen(RectangleF r) =>
        new(_pan.X + r.X * _zoom, _pan.Y + r.Y * _zoom, r.Width * _zoom, r.Height * _zoom);
    private RectangleF ScreenRectToMm(Rectangle r)
    {
        PointF tl = ScreenToMm(r.Left, r.Top), br = ScreenToMm(r.Right, r.Bottom);
        return RectangleF.FromLTRB(tl.X, tl.Y, br.X, br.Y);
    }
    private Rectangle PageRect() => new(
        (int)Math.Round(_pan.X), (int)Math.Round(_pan.Y),
        Math.Max(1, (int)Math.Round(_document.WidthMm * _zoom)),
        Math.Max(1, (int)Math.Round(_document.HeightMm * _zoom)));

    private float SnapMm(float v) => SnapToGrid ? MathF.Round(v / GridStepMm) * GridStepMm : v;
    private PointF SnapMm(PointF p) => new(SnapMm(p.X), SnapMm(p.Y));

    private bool OnPageMm(PointF mm) =>
        mm.X >= 0 && mm.Y >= 0 && mm.X <= _document.WidthMm && mm.Y <= _document.HeightMm;

    // The millimetre region the element layer must cover: the page unioned with every visible element's
    // bounds (so off-page content shows), clamped to what's actually on screen (so the bitmap stays
    // viewport-sized even if an element is dragged far away).
    private RectangleF ContentRenderRegionMm()
    {
        RectangleF region = new(0f, 0f, _document.WidthMm, _document.HeightMm);
        foreach (LabelElement el in _document.Elements)
            if (el.Visible) region = RectangleF.Union(region, el.BoundsMm);
        return RectangleF.Intersect(region, VisibleMm());
    }

    // The millimetre rectangle currently visible in the canvas (bounding box of the four corners,
    // un-rotated for the view spin).
    private RectangleF VisibleMm()
    {
        PointF[] c =
        [
            CornerMm(0, 0), CornerMm(Width, 0), CornerMm(0, Height), CornerMm(Width, Height),
        ];
        float minX = Math.Min(Math.Min(c[0].X, c[1].X), Math.Min(c[2].X, c[3].X));
        float minY = Math.Min(Math.Min(c[0].Y, c[1].Y), Math.Min(c[2].Y, c[3].Y));
        float maxX = Math.Max(Math.Max(c[0].X, c[1].X), Math.Max(c[2].X, c[3].X));
        float maxY = Math.Max(Math.Max(c[0].Y, c[1].Y), Math.Max(c[2].Y, c[3].Y));
        return RectangleF.FromLTRB(minX, minY, maxX, maxY);
    }

    private PointF CornerMm(int sx, int sy)
    {
        PointF p = _viewRotation == 0 ? new PointF(sx, sy) : Unrotate(new PointF(sx, sy));
        return ScreenToMm(p.X, p.Y);
    }

    // --- zoom ---
    public void ZoomTo(float pxPerMm, PointF? screenAnchor = null)
    {
        pxPerMm = Math.Clamp(pxPerMm, MinZoom, MaxZoom);
        PointF anchor = screenAnchor ?? new PointF(Width / 2f, Height / 2f);
        PointF mmUnder = ScreenToMm(anchor.X, anchor.Y);
        _zoom = pxPerMm;
        _pan = new PointF(anchor.X - mmUnder.X * _zoom, anchor.Y - mmUnder.Y * _zoom);
        _userAdjusted = true;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>100% = one printer dot per screen pixel (<see cref="Units.DotsPerMm"/> px/mm).</summary>
    public void ZoomToHundred() => ZoomTo((float)Units.DotsPerMm);

    public void ZoomToFit()
    {
        _userAdjusted = false;   // re-enter auto-fit mode
        if (Width <= 0 || Height <= 0) return;
        float availW = Width - RulerSize - 28, availH = Height - RulerSize - 28;
        if (availW <= 0 || availH <= 0) return;
        // When spun 90/270 the label's on-screen extents are swapped, so fit against those.
        float effW = QuarterTurned ? _document.HeightMm : _document.WidthMm;
        float effH = QuarterTurned ? _document.WidthMm : _document.HeightMm;
        float z = Math.Clamp(Math.Min(availW / effW, availH / effH), MinZoom, MaxZoom);
        _zoom = z;
        // Centre the page centre in the available area; the spin pivots about it, so it stays centred.
        float cx = RulerSize + (Width - RulerSize) / 2f;
        float cy = RulerSize + (Height - RulerSize) / 2f;
        _pan = new PointF(cx - _document.WidthMm * z / 2f, cy - _document.HeightMm * z / 2f);
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_userAdjusted && Width > 0 && Height > 0) ZoomToFit();
    }

    // --- painting ---
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);

        // View-only spin: rotate everything (page, grid, elements, handles, marquee) about the page
        // centre. Rulers are drawn afterwards, outside this transform, so they stay screen-aligned.
        GraphicsState? spun = null;
        if (_viewRotation != 0)
        {
            spun = g.Save();
            PointF c = PageCenterScreen();
            g.TranslateTransform(c.X, c.Y);
            g.RotateTransform(_viewRotation);
            g.TranslateTransform(-c.X, -c.Y);
        }

        Rectangle page = PageRect();

        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            g.FillRectangle(shadow, page.X + 4, page.Y + 4, page.Width, page.Height);
        g.FillRectangle(Brushes.White, page);

        if (ShowGrid) DrawGrid(g, page);

        // Elements on a transparent layer covering the visible region (page + any off-page content, so
        // an element dragged off the page stays visible over the desk). The layer is page-mm-space, so
        // the view-rotation transform above still applies; it's bounded to the viewport to cap its size.
        RectangleF region = ContentRenderRegionMm();
        if (region.Width >= 0.5f && region.Height >= 0.5f)
        {
            var size = new Size(
                Math.Max(1, (int)Math.Ceiling(region.Width * _zoom)),
                Math.Max(1, (int)Math.Ceiling(region.Height * _zoom)));
            using Bitmap layer = LabelRenderer.Render(
                _document, RenderContext.ForScreen(_zoom), Color.Transparent,
                contentOffsetMm: new PointF(-region.X, -region.Y), outputSize: size);
            int dx = (int)Math.Round(_pan.X + region.X * _zoom);
            int dy = (int)Math.Round(_pan.Y + region.Y * _zoom);
            g.DrawImage(layer,
                new Rectangle(dx, dy, layer.Width, layer.Height),
                new Rectangle(0, 0, layer.Width, layer.Height), GraphicsUnit.Pixel);
        }

        g.DrawRectangle(Pens.Black, page);
        DrawSafeMargin(g, page);
        DrawSmartGuides(g, page);
        DrawSelection(g);

        if (_drag == DragMode.Marquee && _marquee.Width > 0 && _marquee.Height > 0)
        {
            using var fill = new SolidBrush(Color.FromArgb(40, Color.RoyalBlue));
            using var pen = new Pen(Color.RoyalBlue) { DashStyle = DashStyle.Dash };
            g.FillRectangle(fill, _marquee);
            g.DrawRectangle(pen, _marquee);
        }

        if (spun is not null) g.Restore(spun);   // back to screen space for the rulers

        DrawRulers(g, page);   // last, so the gutter covers anything scrolled under it
    }

    private void DrawGrid(Graphics g, Rectangle page)
    {
        if (GridStepMm <= 0 || GridStepMm * _zoom < 4f) return;   // too dense to be useful
        using var pen = new Pen(Color.FromArgb(232, 232, 232));
        for (float x = 0; x <= _document.WidthMm + 1e-3f; x += GridStepMm)
        {
            int sx = (int)Math.Round(_pan.X + x * _zoom);
            g.DrawLine(pen, sx, page.Top, sx, page.Bottom);
        }
        for (float y = 0; y <= _document.HeightMm + 1e-3f; y += GridStepMm)
        {
            int sy = (int)Math.Round(_pan.Y + y * _zoom);
            g.DrawLine(pen, page.Left, sy, page.Right, sy);
        }
    }

    // Red "safe area" guide, inset SafeMarginMm from the label edges (on-screen only, never printed).
    private void DrawSafeMargin(Graphics g, Rectangle page)
    {
        if (!ShowSafeMargin || SafeMarginMm <= 0) return;
        float inset = SafeMarginMm * _zoom;
        if (page.Width - 2 * inset < 2 || page.Height - 2 * inset < 2) return;
        var safe = new RectangleF(page.X + inset, page.Y + inset,
            page.Width - 2 * inset, page.Height - 2 * inset);
        using var pen = new Pen(Color.Red) { DashStyle = DashStyle.Dash };
        g.DrawRectangle(pen, safe.X, safe.Y, safe.Width, safe.Height);
    }

    // Magenta alignment guides spanning the page, drawn while dragging when a moving edge/centre snaps;
    // plus equal-spacing indicators (bracketed gap segments) when distributing.
    private void DrawSmartGuides(Graphics g, Rectangle page)
    {
        using var pen = new Pen(Color.FromArgb(230, 0, 140)) { DashStyle = DashStyle.Dash };
        foreach (GuideLine gd in _guides)
        {
            if (gd.Vertical)
            {
                int sx = (int)Math.Round(_pan.X + gd.PositionMm * _zoom);
                g.DrawLine(pen, sx, page.Top, sx, page.Bottom);
            }
            else
            {
                int sy = (int)Math.Round(_pan.Y + gd.PositionMm * _zoom);
                g.DrawLine(pen, page.Left, sy, page.Right, sy);
            }
        }

        using var span = new Pen(Color.FromArgb(230, 0, 140), 1f);
        foreach (SpacingSpan s in _spacingSpans)
        {
            if (s.Horizontal)
            {
                int x1 = (int)Math.Round(_pan.X + s.StartMm * _zoom);
                int x2 = (int)Math.Round(_pan.X + s.EndMm * _zoom);
                int y = (int)Math.Round(_pan.Y + s.CrossMm * _zoom);
                g.DrawLine(span, x1, y, x2, y);
                g.DrawLine(span, x1, y - 4, x1, y + 4);   // end ticks
                g.DrawLine(span, x2, y - 4, x2, y + 4);
            }
            else
            {
                int y1 = (int)Math.Round(_pan.Y + s.StartMm * _zoom);
                int y2 = (int)Math.Round(_pan.Y + s.EndMm * _zoom);
                int x = (int)Math.Round(_pan.X + s.CrossMm * _zoom);
                g.DrawLine(span, x, y1, x, y2);
                g.DrawLine(span, x - 4, y1, x + 4, y1);
                g.DrawLine(span, x - 4, y2, x + 4, y2);
            }
        }
    }

    private void DrawSelection(Graphics g)
    {
        using var pen = new Pen(Color.RoyalBlue, 1f) { DashStyle = DashStyle.Dash };
        foreach (LabelElement el in _selection)
        {
            RectangleF r = MmRectToScreen(el.BoundsMm);
            GraphicsState? spun = PushElementRotation(g, el, r);   // box follows the element's rotation
            g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            if (spun is not null) g.Restore(spun);
        }
        // Resize handles only for a single, unlocked element.
        if (_selection.Count == 1 && !_selection[0].Locked)
        {
            LabelElement el = _selection[0];
            RectangleF r = MmRectToScreen(el.BoundsMm);
            GraphicsState? spun = PushElementRotation(g, el, r);
            using var edge = new Pen(Color.RoyalBlue);
            for (int i = 0; i < HandleFactors.Length; i++)
            {
                RectangleF h = HandleRect(r, i);
                g.FillRectangle(Brushes.White, h);
                g.DrawRectangle(edge, h.X, h.Y, h.Width, h.Height);
            }
            // Rotation knob above the top-centre handle (drawn in the same rotated frame).
            float cx = r.X + r.Width / 2f;
            g.DrawLine(edge, cx, r.Y, cx, r.Y - RotateHandleOffsetPx + RotateHandleRadiusPx);
            var knob = new RectangleF(cx - RotateHandleRadiusPx, r.Y - RotateHandleOffsetPx - RotateHandleRadiusPx,
                RotateHandleRadiusPx * 2, RotateHandleRadiusPx * 2);
            g.FillEllipse(Brushes.White, knob);
            g.DrawEllipse(edge, knob);
            if (spun is not null) g.Restore(spun);
        }
    }

    private static PointF RectCenter(RectangleF r) => new(r.X + r.Width / 2f, r.Y + r.Height / 2f);

    /// <summary>Rotates a point by <paramref name="deg"/> (clockwise) about <paramref name="c"/>.</summary>
    private static PointF RotatePoint(PointF p, PointF c, float deg)
    {
        double rad = deg * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        float dx = p.X - c.X, dy = p.Y - c.Y;
        return new PointF(c.X + (float)(dx * cos - dy * sin), c.Y + (float)(dx * sin + dy * cos));
    }

    /// <summary>Rotates the graphics about a screen-space element rectangle's centre by the element's
    /// rotation, so its selection outline/handles draw aligned with the rotated element. Returns the
    /// saved state to restore (null when the element is unrotated).</summary>
    private static GraphicsState? PushElementRotation(Graphics g, LabelElement el, RectangleF screenRect)
    {
        if (el.Rotation == 0f) return null;
        GraphicsState s = g.Save();
        PointF c = RectCenter(screenRect);
        g.TranslateTransform(c.X, c.Y);
        g.RotateTransform(el.Rotation);
        g.TranslateTransform(-c.X, -c.Y);
        return s;
    }

    private void DrawRulers(Graphics g, Rectangle page)
    {
        using var band = new SolidBrush(Color.FromArgb(238, 238, 240));
        g.FillRectangle(band, 0, 0, Width, RulerSize);
        g.FillRectangle(band, 0, 0, RulerSize, Height);
        using var line = new Pen(Color.FromArgb(180, 180, 184));
        g.DrawLine(line, 0, RulerSize, Width, RulerSize);
        g.DrawLine(line, RulerSize, 0, RulerSize, Height);

        float step = NiceRulerStepMm();
        using var tick = new Pen(Color.FromArgb(120, 120, 124));
        using var text = new SolidBrush(Color.FromArgb(70, 70, 74));

        for (float x = 0; x <= _document.WidthMm + 1e-3f; x += step)
        {
            int sx = (int)Math.Round(_pan.X + x * _zoom);
            if (sx < RulerSize || sx > Width) continue;
            g.DrawLine(tick, sx, RulerSize - 5, sx, RulerSize);
            g.DrawString(RulerLabel(x), _rulerFont, text, sx + 1, 1);
        }
        for (float y = 0; y <= _document.HeightMm + 1e-3f; y += step)
        {
            int sy = (int)Math.Round(_pan.Y + y * _zoom);
            if (sy < RulerSize || sy > Height) continue;
            g.DrawLine(tick, RulerSize - 5, sy, RulerSize, sy);
            g.DrawString(RulerLabel(y), _rulerFont, text, 1, sy + 1);
        }
    }

    private string RulerLabel(float mm) => UnitFormat.Format(mm, _unit, withSuffix: false);

    private float NiceRulerStepMm()
    {
        // Tick spacing chosen so labels are round numbers in the active unit (inch steps are mm multiples).
        ReadOnlySpan<float> steps = _unit == MeasurementUnit.Inches
            ? [2.54f, 6.35f, 12.7f, 25.4f, 50.8f, 127f, 254f]    // 0.1, 0.25, 0.5, 1, 2, 5, 10 in
            : [1, 2, 5, 10, 20, 50, 100];
        foreach (float s in steps)
            if (s * _zoom >= 34f) return s;
        return steps[^1];
    }

    private static RectangleF HandleRect(RectangleF r, int i)
    {
        (float fx, float fy) = HandleFactors[i];
        return new RectangleF(
            r.X + r.Width * fx - HandleSize / 2f, r.Y + r.Height * fy - HandleSize / 2f,
            HandleSize, HandleSize);
    }

    // --- hit testing ---
    private LabelElement? HitElement(PointF mm) =>
        _document.Elements
            .Where(el => el.Visible && el.BoundsMm.Contains(mm))
            .OrderByDescending(el => el.ZOrder)   // topmost first
            .FirstOrDefault();

    // Topmost element whose rendering actually covers the point (within a small tolerance). Each
    // candidate is rendered alone and its alpha sampled, so transparent areas — an unfilled shape's
    // interior, a PNG's transparency — let the click fall through to whatever is painted behind.
    private LabelElement? HitPixel(PointF mm)
    {
        float tolMm = HitTolerancePx / _zoom;
        IEnumerable<LabelElement> candidates = _document.Elements
            .Where(el => el.Visible && RectangleF.Inflate(LabelBounds.RotatedBoundsMm(el), tolMm, tolMm).Contains(mm))
            .OrderByDescending(el => el.ZOrder);
        foreach (LabelElement el in candidates)
            if (ElementHitTest.PaintsAt(el, mm, tolMm, _zoom)) return el;
        return null;
    }

    private int HitHandle(Point p)
    {
        if (_selection.Count != 1 || _selection[0].Locked) return -1;   // locked → no resize
        LabelElement el = _selection[0];
        RectangleF r = MmRectToScreen(el.BoundsMm);
        // Handles are drawn rotated with the element, so test the mouse in the element's local frame.
        PointF lp = el.Rotation == 0f ? p : RotatePoint(p, RectCenter(r), -el.Rotation);
        for (int i = 0; i < HandleFactors.Length; i++)
            if (HandleRect(r, i).Contains(lp)) return i;
        return -1;
    }

    // True when the cursor is over the rotation knob (single, unlocked element). Like HitHandle, the test
    // is done in the element's local (unrotated) frame, since the knob is drawn rotated with the element.
    private bool HitRotateHandle(Point p)
    {
        if (_selection.Count != 1 || _selection[0].Locked) return false;
        LabelElement el = _selection[0];
        RectangleF r = MmRectToScreen(el.BoundsMm);
        PointF lp = el.Rotation == 0f ? p : RotatePoint(p, RectCenter(r), -el.Rotation);
        var knob = new PointF(r.X + r.Width / 2f, r.Y - RotateHandleOffsetPx);
        return MathF.Sqrt((lp.X - knob.X) * (lp.X - knob.X) + (lp.Y - knob.Y) * (lp.Y - knob.Y))
               <= RotateHandleRadiusPx + 3f;
    }

    private static float AngleDeg(PointF centre, PointF p) =>
        (float)(Math.Atan2(p.Y - centre.Y, p.X - centre.X) * 180.0 / Math.PI);

    // --- mouse ---
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        e = Rotated(e);

        if (e.Button == MouseButtons.Middle)
        {
            _drag = DragMode.Pan;
            _dragStartScreen = e.Location;
            _panOrigin = _pan;
            _userAdjusted = true;
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            // Select the element under the cursor (if any, and not already selected) so the
            // context menu acts on it; the menu itself opens automatically on mouse-up.
            LabelElement? target = HitPixel(ScreenToMm(e.X, e.Y));
            if (target is not null && !_selection.Contains(target))
            {
                _selection.Clear();
                _selection.Add(target);
                OnSelectionChanged();
                Invalidate();
            }
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        _dragStartScreen = e.Location;
        _dragStartMm = ScreenToMm(e.X, e.Y);
        bool ctrl = (ModifierKeys & Keys.Control) != 0;

        if (HitRotateHandle(e.Location))
        {
            LabelElement el = _selection[0];
            _drag = DragMode.Rotate;
            _rotateCenter = RectCenter(MmRectToScreen(el.BoundsMm));
            _rotateStartRotation = el.Rotation;
            _rotateStartPointerAngle = AngleDeg(_rotateCenter, e.Location);
            return;
        }

        int handle = HitHandle(e.Location);
        if (handle >= 0)
        {
            _drag = DragMode.Resize;
            _resizeHandle = handle;
            CaptureOrig();
            return;
        }

        // Pixel-accurate pick: select the element actually painted under the cursor ("click what you
        // see"), so an unfilled shape's empty interior lets the click fall through to elements behind it.
        LabelElement? hit = HitPixel(_dragStartMm);
        if (hit is not null)
        {
            if (ctrl)
            {
                if (!_selection.Remove(hit)) _selection.Add(hit);
                OnSelectionChanged();
            }
            else if (!_selection.Contains(hit))
            {
                _selection.Clear();
                _selection.Add(hit);
                ExpandSelectionToGroups();
                OnSelectionChanged();
            }
            // Only start a move if the selection has at least one unlocked element to move.
            if (_selection.Contains(hit) && _selection.Any(el => !el.Locked))
            {
                _drag = DragMode.Move;
                CaptureOrig();
            }
        }
        else if (OnPageMm(_dragStartMm))
        {
            // Empty spot inside the page → marquee-select.
            if (!ctrl && _selection.Count > 0) { _selection.Clear(); OnSelectionChanged(); }
            _drag = DragMode.Marquee;
            _marquee = new Rectangle(e.Location, Size.Empty);
        }
        else
        {
            // Empty spot on the gray desk → pan the view (a click without dragging deselects).
            _drag = DragMode.Pan;
            _panFromDesk = true;
            _dragStartScreen = e.Location;
            _panOrigin = _pan;
            _userAdjusted = true;
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        e = Rotated(e);
        switch (_drag)
        {
            case DragMode.Pan:
                _pan = new PointF(_panOrigin.X + (e.X - _dragStartScreen.X),
                                  _panOrigin.Y + (e.Y - _dragStartScreen.Y));
                Invalidate();
                break;

            case DragMode.Move:
            {
                PointF mm = ScreenToMm(e.X, e.Y);
                float dx = mm.X - _dragStartMm.X, dy = mm.Y - _dragStartMm.Y;
                bool snappedX = false, snappedY = false;
                _guides.Clear();
                _spacingSpans.Clear();

                // Smart alignment: nudge the moving box onto nearby edges/centres (Alt bypasses).
                if (ShowSmartGuides && (ModifierKeys & Keys.Alt) == 0)
                {
                    IReadOnlyList<RectangleF> targets = _document.Elements
                        .Where(el => el.Visible && !_dragOrig.ContainsKey(el))
                        .Select(el => el.BoundsMm).ToList();
                    float tolMm = GuideTolerancePx / _zoom;

                    RectangleF box = OffsetRect(MovingOrigBounds(), dx, dy);
                    AlignmentSnap snap = AlignmentGuides.Snap(
                        box, targets, new SizeF(_document.WidthMm, _document.HeightMm), tolMm);
                    dx += snap.OffsetXMm;
                    dy += snap.OffsetYMm;
                    _guides.AddRange(snap.Guides);
                    snappedX = snap.Guides.Any(gd => gd.Vertical);
                    snappedY = snap.Guides.Any(gd => !gd.Vertical);

                    // Equal-spacing: on any axis edge/centre alignment didn't claim, snap gaps even.
                    DistributionSnap dist = AlignmentGuides.Distribute(OffsetRect(MovingOrigBounds(), dx, dy), targets, tolMm);
                    bool applyDistX = !snappedX && dist.FoundX, applyDistY = !snappedY && dist.FoundY;
                    if (applyDistX) { dx += dist.OffsetXMm; snappedX = true; }
                    if (applyDistY) { dy += dist.OffsetYMm; snappedY = true; }
                    // Show only the indicators for the axis we actually equalised (Horizontal span = X axis).
                    foreach (SpacingSpan s in dist.Spans)
                        if ((s.Horizontal && applyDistX) || (!s.Horizontal && applyDistY)) _spacingSpans.Add(s);
                }

                foreach (LabelElement el in _selection)
                {
                    if (el.Locked) continue;            // locked elements don't move
                    RectangleF o = _dragOrig[el];
                    float nx = o.X + dx, ny = o.Y + dy;
                    el.XMm = snappedX ? nx : SnapMm(nx);   // grid-snap only the axes smart guides didn't
                    el.YMm = snappedY ? ny : SnapMm(ny);
                }
                Invalidate();
                break;
            }

            case DragMode.Resize when _selection.Count == 1:
                ApplyResize(_selection[0], ScreenToMm(e.X, e.Y));   // snapped in local frame inside
                Invalidate();
                break;

            case DragMode.Rotate when _selection.Count == 1:
            {
                float delta = AngleDeg(_rotateCenter, e.Location) - _rotateStartPointerAngle;
                float rot = _rotateStartRotation + delta;
                rot = (ModifierKeys & Keys.Shift) != 0 ? MathF.Round(rot / 15f) * 15f : MathF.Round(rot);
                rot = ((rot % 360f) + 360f) % 360f;   // normalise to [0, 360)
                _selection[0].Rotation = rot;
                Invalidate();
                break;
            }

            case DragMode.Marquee:
                _marquee = Rectangle.FromLTRB(
                    Math.Min(_dragStartScreen.X, e.X), Math.Min(_dragStartScreen.Y, e.Y),
                    Math.Max(_dragStartScreen.X, e.X), Math.Max(_dragStartScreen.Y, e.Y));
                Invalidate();
                break;

            case DragMode.None:
            {
                if (HitRotateHandle(e.Location)) { Cursor = Cursors.Hand; break; }   // rotation knob
                int handle = HitHandle(e.Location);
                if (handle >= 0) { Cursor = CursorForHandle(handle); break; }
                PointF mm = ScreenToMm(e.X, e.Y);
                // Over the gray desk (no element) → show the pan cursor as an affordance.
                Cursor = !OnPageMm(mm) && HitElement(mm) is null ? Cursors.SizeAll : Cursors.Default;
                break;
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        switch (_drag)
        {
            case DragMode.Move or DragMode.Resize:
                CommitDrag(_drag == DragMode.Resize ? "Resize" : "Move");
                break;

            case DragMode.Rotate when _selection.Count == 1:
                CommitRotate(_selection[0], _rotateStartRotation);
                break;

            case DragMode.Marquee:
            {
                RectangleF mmRect = ScreenRectToMm(_marquee);
                if ((ModifierKeys & Keys.Control) == 0) _selection.Clear();
                foreach (LabelElement el in _document.Elements)
                    if (el.Visible && el.BoundsMm.IntersectsWith(mmRect) && !_selection.Contains(el))
                        _selection.Add(el);
                ExpandSelectionToGroups();
                OnSelectionChanged();
                break;
            }

            case DragMode.Pan when _panFromDesk && _pan == _panOrigin:
                // A desk click that didn't drag → deselect (matches clicking empty space).
                if ((ModifierKeys & Keys.Control) == 0 && _selection.Count > 0)
                { _selection.Clear(); OnSelectionChanged(); }
                break;
        }
        _drag = DragMode.None;
        _resizeHandle = -1;
        _panFromDesk = false;
        _marquee = Rectangle.Empty;
        _guides.Clear();
        _spacingSpans.Clear();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        e = Rotated(e);
        float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        ZoomTo(_zoom * factor, e.Location);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        e = Rotated(e);
        if (e.Button != MouseButtons.Left) return;
        if (HitPixel(ScreenToMm(e.X, e.Y)) is TextElement t && !t.Locked)
            BeginEditText(t);
    }

    // --- inline text editing (double-click a TextElement) ---
    private void BeginEditText(TextElement t)
    {
        CommitTextEdit();   // finish any prior edit first
        _editingText = t;
        _editStartText = t.Text;

        RectangleF r = MmRectToScreen(t.BoundsMm);
        FontStyle style = FontStyle.Regular;
        if (t.Bold) style |= FontStyle.Bold;
        if (t.Italic) style |= FontStyle.Italic;
        float px = Math.Max(8f, (float)(t.FontSizePt / 72.0 * _zoom * Units.MmPerInch));

        _textEditor = new TextBox
        {
            Multiline = true,
            BorderStyle = BorderStyle.FixedSingle,
            Text = t.Text,
            Bounds = Rectangle.Round(r),
            Font = new Font(t.FontFamily, px, style, GraphicsUnit.Pixel),
            TextAlign = t.Alignment switch
            {
                TextAlignment.Center => HorizontalAlignment.Center,
                TextAlignment.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Left,
            },
        };
        _textEditor.MinimumSize = new Size(24, 16);
        _textEditor.KeyDown += TextEditorKeyDown;
        _textEditor.LostFocus += (_, _) => CommitTextEdit();
        Controls.Add(_textEditor);
        _textEditor.Focus();
        _textEditor.SelectAll();
        Invalidate();
    }

    private void TextEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; CommitTextEdit(); Focus(); }
        else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelTextEdit(); Focus(); }
    }

    private void CommitTextEdit()
    {
        if (_textEditor is null || _editingText is null) return;
        // Capture and clear state first: disposing the focused editor fires LostFocus, which would
        // otherwise re-enter this method.
        TextBox editor = _textEditor;
        TextElement t = _editingText;
        string newText = editor.Text, oldText = _editStartText;
        DisposeEditor(editor);

        if (newText != oldText)
        {
            if (History is not null)
                History.Execute(new DelegateCommand("Edit text",
                    () => { t.Text = newText; t.FitToContent(); },
                    () => { t.Text = oldText; t.FitToContent(); }));
            else { t.Text = newText; t.FitToContent(); }
            OnDocumentChanged();
        }
        Invalidate();
    }

    private void CancelTextEdit()
    {
        if (_textEditor is null) return;
        DisposeEditor(_textEditor);
        Invalidate();
    }

    private void DisposeEditor(TextBox editor)
    {
        _textEditor = null;
        _editingText = null;
        editor.KeyDown -= TextEditorKeyDown;
        Controls.Remove(editor);
        editor.Dispose();
    }

    private void ApplyResize(LabelElement el, PointF m)
    {
        RectangleF o = _dragOrig[el];
        _guides.Clear();
        // For a rotated element, resize in its local frame: un-rotate the mouse about the (fixed)
        // original centre, then snap. Identity when the element is unrotated.
        if (el.Rotation != 0f) m = RotatePoint(m, RectCenter(o), -el.Rotation);
        m = SnapMm(m);

        // Hold Shift on a corner handle to keep the original aspect ratio.
        if ((ModifierKeys & Keys.Shift) != 0 && _resizeHandle is 0 or 2 or 4 or 6 && o.Width > 0 && o.Height > 0)
        {
            ApplyResizeAspect(el, o, m);
            return;
        }

        float left = o.Left, top = o.Top, right = o.Right, bottom = o.Bottom;
        switch (_resizeHandle)
        {
            case 0: left = m.X; top = m.Y; break;
            case 1: top = m.Y; break;
            case 2: right = m.X; top = m.Y; break;
            case 3: right = m.X; break;
            case 4: right = m.X; bottom = m.Y; break;
            case 5: bottom = m.Y; break;
            case 6: left = m.X; bottom = m.Y; break;
            case 7: left = m.X; break;
        }

        // Resize-time smart guides: snap the dragged edge(s) to other elements' edges/centres + the page
        // (unrotated elements only; Alt bypasses). Draws the matched guide lines.
        if (ShowSmartGuides && (ModifierKeys & Keys.Alt) == 0 && el.Rotation == 0f)
        {
            CandidateLines(el, out List<float> vx, out List<float> hy);
            float tol = GuideTolerancePx / _zoom;
            if (_resizeHandle is 0 or 6 or 7) left = SnapEdge(left, vx, tol, vertical: true);
            if (_resizeHandle is 2 or 3 or 4) right = SnapEdge(right, vx, tol, vertical: true);
            if (_resizeHandle is 0 or 1 or 2) top = SnapEdge(top, hy, tol, vertical: false);
            if (_resizeHandle is 4 or 5 or 6) bottom = SnapEdge(bottom, hy, tol, vertical: false);
        }

        float x = Math.Min(left, right), y = Math.Min(top, bottom);
        float w = Math.Max(MinElementMm, Math.Abs(right - left));
        float h = Math.Max(MinElementMm, Math.Abs(bottom - top));
        el.BoundsMm = new RectangleF(x, y, w, h);
    }

    // Aspect-locked corner resize: keep the corner opposite the dragged handle fixed and preserve w:h.
    private void ApplyResizeAspect(LabelElement el, RectangleF o, PointF m)
    {
        PointF anchor = _resizeHandle switch
        {
            0 => new PointF(o.Right, o.Bottom),
            2 => new PointF(o.Left, o.Bottom),
            4 => new PointF(o.Left, o.Top),
            _ => new PointF(o.Right, o.Top),   // 6
        };
        float w = Math.Max(MinElementMm, Math.Abs(m.X - anchor.X));
        float h = Math.Max(MinElementMm, Math.Abs(m.Y - anchor.Y));
        float ratio = o.Width / o.Height;
        if (w / o.Width >= h / o.Height) h = w / ratio; else w = h * ratio;
        w = Math.Max(MinElementMm, w);
        h = Math.Max(MinElementMm, h);
        float x = _resizeHandle is 2 or 4 ? anchor.X : anchor.X - w;
        float y = _resizeHandle is 4 or 6 ? anchor.Y : anchor.Y - h;
        el.BoundsMm = new RectangleF(x, y, w, h);
    }

    // Snap a single edge coordinate to the nearest candidate line within tolerance; records the matched
    // guide for drawing and returns the (possibly snapped) coordinate.
    private float SnapEdge(float coord, List<float> candidates, float tol, bool vertical)
    {
        float best = tol, snapped = coord;
        bool found = false;
        foreach (float c in candidates)
        {
            float d = MathF.Abs(c - coord);
            if (d <= best) { best = d; snapped = c; found = true; }
        }
        if (found) _guides.Add(new GuideLine(vertical, snapped));
        return snapped;
    }

    // Candidate snap lines (mm) from every other visible element's edges/centres + the page edges/centre.
    private void CandidateLines(LabelElement self, out List<float> vx, out List<float> hy)
    {
        vx = []; hy = [];
        foreach (LabelElement t in _document.Elements)
        {
            if (ReferenceEquals(t, self) || !t.Visible) continue;
            RectangleF b = t.BoundsMm;
            vx.Add(b.Left); vx.Add(b.Left + b.Width / 2f); vx.Add(b.Right);
            hy.Add(b.Top); hy.Add(b.Top + b.Height / 2f); hy.Add(b.Bottom);
        }
        vx.Add(0); vx.Add(_document.WidthMm / 2f); vx.Add(_document.WidthMm);
        hy.Add(0); hy.Add(_document.HeightMm / 2f); hy.Add(_document.HeightMm);
    }

    private void CaptureOrig()
    {
        _dragOrig.Clear();
        foreach (LabelElement el in _selection) _dragOrig[el] = el.BoundsMm;
    }

    // Union of the original bounds of the elements being moved (unlocked) — the box smart guides align.
    private RectangleF MovingOrigBounds()
    {
        RectangleF? u = null;
        foreach (LabelElement el in _selection)
        {
            if (el.Locked || !_dragOrig.TryGetValue(el, out RectangleF o)) continue;
            u = u is null ? o : RectangleF.Union(u.Value, o);
        }
        return u ?? RectangleF.Empty;
    }

    private static RectangleF OffsetRect(RectangleF r, float dx, float dy) =>
        new(r.X + dx, r.Y + dy, r.Width, r.Height);

    // On mouse-up after a move/resize, record one undoable command for the whole gesture.
    private void CommitDrag(string name)
    {
        List<LabelElement> els = [.. _selection.Where(el => _dragOrig.ContainsKey(el) && !el.Locked)];
        if (els.Count > 0 && History is not null)
        {
            // Only bounds changed during the gesture; rotation/flip are unchanged.
            ElementGeometry[] before = [.. els.Select(el =>
                new ElementGeometry(_dragOrig[el], el.Rotation, el.FlipH, el.FlipV))];
            ElementGeometry[] after = [.. els.Select(ElementGeometry.Capture)];
            if (!before.SequenceEqual(after))
                History.PushExecuted(new GeometryCommand(name, els, before, after));
        }
        OnDocumentChanged();
    }

    // Records a free-rotation gesture as one undoable command (bounds unchanged; only Rotation differs).
    private void CommitRotate(LabelElement el, float startRotation)
    {
        if (History is not null && el.Rotation != startRotation)
        {
            var before = new ElementGeometry(el.BoundsMm, startRotation, el.FlipH, el.FlipV);
            History.PushExecuted(new GeometryCommand("Rotate", [el], [before], [ElementGeometry.Capture(el)]));
        }
        OnDocumentChanged();
    }

    private static Cursor CursorForHandle(int handle) => handle switch
    {
        0 or 4 => Cursors.SizeNWSE,
        2 or 6 => Cursors.SizeNESW,
        1 or 5 => Cursors.SizeNS,
        3 or 7 => Cursors.SizeWE,
        _ => Cursors.Default,
    };

    // --- keyboard ---
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Delete => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_selection.Count == 0) return;

        if (e.KeyCode == Keys.Delete)
        {
            DeleteSelection();
            e.Handled = true;
            return;
        }

        float step = e.Shift ? 5f : e.Control ? 0.1f : 1f;
        float dx = 0, dy = 0;
        switch (e.KeyCode)
        {
            case Keys.Left: dx = -step; break;
            case Keys.Right: dx = step; break;
            case Keys.Up: dy = -step; break;
            case Keys.Down: dy = step; break;
            default: return;
        }
        List<LabelElement> nudged = [.. _selection.Where(el => !el.Locked)];   // locked don't nudge
        if (nudged.Count == 0) { e.Handled = true; return; }
        if (History is not null)
            History.Execute(GeometryCommand.Move("Nudge", nudged, dx, dy));
        else
            foreach (LabelElement el in nudged) { el.XMm += dx; el.YMm += dy; }
        OnDocumentChanged();
        Invalidate();
        e.Handled = true;
    }

    private void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);
    private void OnDocumentChanged() => DocumentChanged?.Invoke(this, EventArgs.Empty);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _rulerFont.Dispose();
        base.Dispose(disposing);
    }
}
