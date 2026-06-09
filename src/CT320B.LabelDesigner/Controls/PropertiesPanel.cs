using System.Drawing;
using CT320B.LabelDesigner.Core.Editing;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Model.Elements;
using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// Edits the single selected element. The top section is common to all elements (name, position/size
/// in mm, rotation, mirror, visibility, lock); below it a dynamic section shows type-specific editors
/// (text font/colour, shape stroke/fill, QR/barcode data, image file/fit, table rows/cells…). Every
/// edit goes through the undo stack. Disabled when zero or multiple elements are selected.
/// </summary>
public sealed class PropertiesPanel : UserControl
{
    private readonly CanvasControl _canvas;
    private readonly UndoStack _history;

    private readonly TextBox _name = new();
    private readonly NumericUpDown _x = NumUp(-1000, 1000);
    private readonly NumericUpDown _y = NumUp(-1000, 1000);
    private readonly NumericUpDown _w = NumUp(0.1m, 1000);
    private readonly NumericUpDown _h = NumUp(0.1m, 1000);
    private readonly NumericUpDown _rot = NumUp(0, 359, 1m, 0);
    private readonly CheckBox _flipH = new() { Text = Loc.T("FlipH"), AutoSize = true };
    private readonly CheckBox _flipV = new() { Text = Loc.T("FlipV"), AutoSize = true };
    private readonly CheckBox _visible = new() { Text = Loc.T("Visible"), AutoSize = true };
    private readonly CheckBox _locked = new() { Text = Loc.T("Locked"), AutoSize = true };
    private readonly CheckBox _printable = new() { Text = Loc.T("Printable"), AutoSize = true };
    private readonly TableLayoutPanel _baseGrid;
    private readonly TableLayoutPanel _typeGrid;
    private readonly Panel _content;                 // scrolled content host (moved vertically)
    private readonly VScrollBar _scroll;             // custom-width vertical scrollbar (thicker than native)
    private bool _inLayout;
    private readonly Label _typeHeader = new()
    {
        Dock = DockStyle.Top, AutoSize = false, Height = 22, TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Segoe UI", 8.25f, FontStyle.Bold), ForeColor = Color.DimGray,
        Padding = new Padding(2, 2, 0, 0),
    };

    private readonly List<Action> _typeRefreshers = [];

    private LabelElement? _element;
    private bool _populating;
    private ElementGeometry _geomBefore;
    private string _nameBefore = "";

    private MeasurementUnit _unit = MeasurementUnit.Millimeters;
    private Label _xLabel = null!, _yLabel = null!, _wLabel = null!, _hLabel = null!;

    /// <summary>Measurement unit for the length fields (X/Y/W/H + shape/table mm fields). The model stays
    /// in millimetres; this only changes display/entry (Phase 14d). Setting it rebuilds the editors.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public MeasurementUnit Unit
    {
        get => _unit;
        set
        {
            if (_unit == value) return;
            _unit = value;
            ConfigureBaseUnits();
            Bind();   // rebuild type editors in the new unit + repopulate
        }
    }

    public PropertiesPanel(CanvasControl canvas, UndoStack history)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _history = history ?? throw new ArgumentNullException(nameof(history));

        AutoScroll = false;   // we drive scrolling ourselves with a wider, easier-to-grab scrollbar

        _baseGrid = NewGrid();
        BuildBaseFields();
        _typeGrid = NewGrid();

        // The grids live inside a content panel that we slide vertically; a custom VScrollBar (wider
        // than the ~17 px native one) docks to the right. Dock=Top order is outermost-first, so add
        // bottom-to-top (type grid, header, base grid last).
        _content = new Panel { Padding = new Padding(8), BackColor = BackColor };
        _content.Controls.Add(_typeGrid);
        _content.Controls.Add(_typeHeader);
        _content.Controls.Add(_baseGrid);

        _scroll = new VScrollBar { Dock = DockStyle.Right, Width = 22, Visible = false, SmallChange = 24 };
        _scroll.Scroll += (_, e) => _content.Top = -e.NewValue;

        Controls.Add(_content);
        Controls.Add(_scroll);

        // Re-flow whenever the content height changes (rows added/removed as the editors rebuild).
        _baseGrid.SizeChanged += (_, _) => LayoutContent();
        _typeGrid.SizeChanged += (_, _) => LayoutContent();

        _canvas.SelectionChanged += (_, _) => Bind();
        _history.Changed += (_, _) => Populate();
        ConfigureBaseUnits();   // localise the X/Y/W/H labels for the default unit
        Bind();
        LayoutContent();
    }

    // Sizes the content panel to its grids, shows/hides the scrollbar, and applies the scroll offset.
    private void LayoutContent()
    {
        if (_inLayout || _content is null || ClientSize.Height <= 0) return;
        _inLayout = true;
        try
        {
            int viewH = ClientSize.Height;
            _content.Width = ClientSize.Width;
            _content.PerformLayout();
            int contentH = ContentHeight();
            bool needScroll = contentH > viewH;

            int width = ClientSize.Width - (needScroll ? _scroll.Width : 0);
            if (_content.Width != width) { _content.Width = width; _content.PerformLayout(); contentH = ContentHeight(); }

            _content.Height = Math.Max(contentH, viewH);
            _scroll.Visible = needScroll;
            if (needScroll)
            {
                _scroll.Minimum = 0;
                _scroll.LargeChange = Math.Max(1, viewH);
                _scroll.Maximum = contentH;
                int maxScroll = Math.Max(0, contentH - viewH);
                if (_scroll.Value > maxScroll) _scroll.Value = maxScroll;
                _content.Top = -_scroll.Value;
            }
            else
            {
                _scroll.Value = 0;
                _content.Top = 0;
            }
        }
        finally { _inLayout = false; }
    }

    private int ContentHeight() => _content.Padding.Vertical + _baseGrid.Height + _typeHeader.Height + _typeGrid.Height;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutContent();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_scroll.Visible) return;
        int max = Math.Max(0, _scroll.Maximum - _scroll.LargeChange + 1);
        int step = (e.Delta > 0 ? -1 : 1) * _scroll.SmallChange * 2;
        _scroll.Value = Math.Clamp(_scroll.Value + step, 0, max);
        _content.Top = -_scroll.Value;
    }

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Padding = new Padding(0, 2, 0, 0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    // --- base (common) fields ---
    private void BuildBaseFields()
    {
        AddRow(_baseGrid, Loc.T("PropName"), _name);
        _name.Dock = DockStyle.Fill;
        _name.Enter += (_, _) => _nameBefore = _element?.Name ?? "";
        _name.TextChanged += (_, _) => { if (!_populating && _element is not null) _element.Name = _name.Text; };
        _name.Validated += (_, _) => CommitName();

        _xLabel = AddRow(_baseGrid, "X", _x);   // text set per-unit in ConfigureBaseUnits
        _yLabel = AddRow(_baseGrid, "Y", _y);
        _wLabel = AddRow(_baseGrid, "W", _w);
        _hLabel = AddRow(_baseGrid, "H", _h);
        AddRow(_baseGrid, Loc.T("PropRotation"), _rot);
        foreach (NumericUpDown n in new[] { _x, _y, _w, _h, _rot })
        {
            n.Dock = DockStyle.Fill;
            n.Enter += (_, _) => { if (_element is not null) _geomBefore = ElementGeometry.Capture(_element); };
            n.ValueChanged += (_, _) => ApplyGeometryLive();
            n.Validated += (_, _) => CommitGeometry();
        }

        var flips = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        flips.Controls.Add(_flipH);
        flips.Controls.Add(_flipV);
        AddRow(_baseGrid, Loc.T("PropMirror"), flips);

        var toggles = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        toggles.Controls.Add(_visible);
        toggles.Controls.Add(_locked);
        toggles.Controls.Add(_printable);
        AddRow(_baseGrid, "", toggles);

        _flipH.CheckedChanged += (_, _) => ToggleBase("Flip H", e => e.FlipH, (e, v) => e.FlipH = v, _flipH.Checked);
        _flipV.CheckedChanged += (_, _) => ToggleBase("Flip V", e => e.FlipV, (e, v) => e.FlipV = v, _flipV.Checked);
        _visible.CheckedChanged += (_, _) => ToggleBase("Visibility", e => e.Visible, (e, v) => e.Visible = v, _visible.Checked);
        _locked.CheckedChanged += (_, _) => ToggleBase("Lock", e => e.Locked, (e, v) => e.Locked = v, _locked.Checked);
        _printable.CheckedChanged += (_, _) => ToggleBase("Printable", e => e.Printable, (e, v) => e.Printable = v, _printable.Checked);
    }

    private static Label AddRow(TableLayoutPanel grid, string label, Control control)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 0) };
        grid.Controls.Add(lbl, 0, row);
        control.Margin = new Padding(0, 2, 0, 2);
        grid.Controls.Add(control, 1, row);
        return lbl;
    }

    private static NumericUpDown NumUp(decimal min, decimal max, decimal inc = 0.5m, int decimals = 1) =>
        new() { Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = decimals };

    // --- unit-aware length helpers (model is always mm) ---
    private decimal ToDisplay(float mm) => (decimal)UnitFormat.ToDisplay(mm, _unit);
    private float FromDisplay(decimal v) => UnitFormat.ToMm((float)v, _unit);

    // Reconfigures the X/Y/W/H spinners (range/step/decimals + labels) for the active unit. The mm ranges
    // are -1000..1000 (position) and 0.1..1000 (size); converted to the display unit here.
    private void ConfigureBaseUnits()
    {
        int dec = UnitFormat.Decimals(_unit);
        decimal inc = UnitFormat.Increment(_unit);
        string u = UnitFormat.Suffix(_unit);
        SetRange(_x, -1000, 1000, dec, inc); SetRange(_y, -1000, 1000, dec, inc);
        SetRange(_w, 0.1m, 1000, dec, inc); SetRange(_h, 0.1m, 1000, dec, inc);
        _xLabel.Text = $"{Loc.T("PropX")} ({u})"; _yLabel.Text = $"{Loc.T("PropY")} ({u})";
        _wLabel.Text = $"{Loc.T("PropW")} ({u})"; _hLabel.Text = $"{Loc.T("PropH")} ({u})";
    }

    private void SetRange(NumericUpDown n, decimal minMm, decimal maxMm, int dec, decimal inc)
    {
        n.DecimalPlaces = dec;
        n.Increment = inc;
        n.Minimum = ToDisplay((float)minMm);
        n.Maximum = ToDisplay((float)maxMm);
    }

    // --- binding ---
    private void Bind()
    {
        _element = _canvas.Selection.Count == 1 ? _canvas.Selection[0] : null;
        bool one = _element is not null;
        _baseGrid.Enabled = one;
        _typeHeader.Text = one ? Loc.F("PropertiesHeader", _element!.GetType().Name.Replace("Element", "")) : "";
        BuildTypeEditors();
        Populate();
    }

    private void Populate()
    {
        if (_element is null) return;
        _populating = true;
        try
        {
            _name.Text = _element.Name;
            _x.Value = Clamp(_x, ToDisplay(_element.XMm));
            _y.Value = Clamp(_y, ToDisplay(_element.YMm));
            _w.Value = Clamp(_w, ToDisplay(_element.WidthMm));
            _h.Value = Clamp(_h, ToDisplay(_element.HeightMm));
            _rot.Value = Clamp(_rot, (decimal)_element.Rotation);
            _flipH.Checked = _element.FlipH;
            _flipV.Checked = _element.FlipV;
            _visible.Checked = _element.Visible;
            _locked.Checked = _element.Locked;
            _printable.Checked = _element.Printable;
            foreach (Action refresh in _typeRefreshers) refresh();
            UpdateEnabled();
        }
        finally { _populating = false; }
    }

    private void UpdateEnabled()
    {
        bool editable = _element is not null && !_element.Locked;
        foreach (Control c in new Control[] { _x, _y, _w, _h, _rot, _flipH, _flipV, _printable })
            c.Enabled = editable;
        _typeGrid.Enabled = editable;
    }

    private static decimal Clamp(NumericUpDown n, decimal v) => Math.Clamp(v, n.Minimum, n.Maximum);

    // --- base commits ---
    private void ApplyGeometryLive()
    {
        if (_populating || _element is null) return;
        _element.XMm = FromDisplay(_x.Value);
        _element.YMm = FromDisplay(_y.Value);
        _element.WidthMm = FromDisplay(_w.Value);
        _element.HeightMm = FromDisplay(_h.Value);
        _element.Rotation = (float)_rot.Value;
        _canvas.RefreshDocument();
    }

    private void CommitGeometry()
    {
        if (_element is null) return;
        ElementGeometry after = ElementGeometry.Capture(_element);
        if (after.Equals(_geomBefore)) return;
        _history.PushExecuted(new GeometryCommand("Edit geometry", [_element], [_geomBefore], [after]));
        _geomBefore = after;
    }

    private void CommitName()
    {
        if (_element is null || _name.Text == _nameBefore) return;
        LabelElement el = _element;
        string oldName = _nameBefore, newName = _name.Text;
        el.Name = newName;
        _history.PushExecuted(new DelegateCommand("Rename", () => el.Name = newName, () => el.Name = oldName));
        _nameBefore = newName;
    }

    private void ToggleBase(string name, Func<LabelElement, bool> get, Action<LabelElement, bool> set, bool value)
    {
        if (_populating || _element is null || get(_element) == value) return;
        LabelElement el = _element;
        _history.Execute(new DelegateCommand($"Toggle {name}", () => set(el, value), () => set(el, !value)));
        _canvas.RefreshDocument();
    }

    // --- type-specific editors ---
    private void BuildTypeEditors()
    {
        _typeGrid.SuspendLayout();
        _typeGrid.Controls.Clear();
        _typeGrid.RowStyles.Clear();
        _typeGrid.RowCount = 0;
        _typeRefreshers.Clear();

        switch (_element)
        {
            case TextElement t:
                // Size-affecting edits also re-fit the box (FitToContent is deterministic from
                // text+font+wrap, so it runs in both the apply and undo paths and stays consistent).
                EString(Loc.T("Text"), () => t.Text, v => { t.Text = v; t.FitToContent(); }, multiline: true);
                EFont(Loc.T("Font"), () => t.FontFamily, v => { t.FontFamily = v; t.FitToContent(); });
                EFloat(Loc.T("SizePt"), () => t.FontSizePt, v => { t.FontSizePt = v; t.FitToContent(); }, 4, 300, 1, 1);
                EBool(Loc.T("Bold"), () => t.Bold, v => { t.Bold = v; t.FitToContent(); });
                EBool(Loc.T("Italic"), () => t.Italic, v => { t.Italic = v; t.FitToContent(); });
                EEnum(Loc.T("AlignLabel"), () => t.Alignment, v => t.Alignment = v);
                EBool(Loc.T("Wrap"), () => t.Wrap, v => { t.Wrap = v; t.FitToContent(); });
                EColor(Loc.T("Colour"), () => t.Color, v => t.Color = v);
                break;
            case ShapeElement s:
                EEnum(Loc.T("Kind"), () => s.Kind, v => s.Kind = v);
                EBool(Loc.T("Filled"), () => s.Filled, v => s.Filled = v);
                EFloatMm(Loc.T("Stroke"), () => s.StrokeWidthMm, v => s.StrokeWidthMm = v, 0, 20);
                EEnum(Loc.T("LineStyle"), () => s.StrokeStyle, v => s.StrokeStyle = v);
                EFloatMm(Loc.T("Corner"), () => s.CornerRadiusMm, v => s.CornerRadiusMm = v, 0, 50);
                EInt(Loc.T("Sides"), () => s.Sides, v => s.Sides = v, 3, 20);
                EInt(Loc.T("StarPts"), () => s.StarPoints, v => s.StarPoints = v, 3, 20);
                EFloat(Loc.T("InnerRatio"), () => s.InnerRatio, v => s.InnerRatio = v, 0.05m, 0.95m, 0.05m, 2);
                EFloat(Loc.T("StartAngle"), () => s.StartAngleDeg, v => s.StartAngleDeg = v, 0, 360, 5, 0);
                EFloat(Loc.T("SweepAngle"), () => s.SweepAngleDeg, v => s.SweepAngleDeg = v, -360, 360, 5, 0);
                EColor(Loc.T("Stroke"), () => s.StrokeColor, v => s.StrokeColor = v);
                EColor(Loc.T("Fill"), () => s.FillColor, v => s.FillColor = v);
                break;
            case QrElement q:
                EString(Loc.T("Data"), () => q.Data, v => q.Data = v, multiline: true);
                EEnum(Loc.T("Ecc"), () => q.ErrorCorrection, v => q.ErrorCorrection = v);
                EInt(Loc.T("Margin"), () => q.Margin, v => q.Margin = v, 0, 16);
                EEnum(Loc.T("Module"), () => q.ModuleStyle, v => q.ModuleStyle = v);
                EEnum(Loc.T("Eyes"), () => q.EyeStyle, v => q.EyeStyle = v);
                ELogo(q);
                EInt(Loc.T("LogoPct"), () => q.LogoScalePercent, v => q.LogoScalePercent = v, 8, 40);
                break;
            case BarcodeElement b:
                EString(Loc.T("Data"), () => b.Data, v => b.Data = v);
                EEnum(Loc.T("Symbology"), () => b.Symbology, v => b.Symbology = v);
                EBool(Loc.T("ShowText"), () => b.ShowText, v => b.ShowText = v);
                break;
            case ImageElement im:
                EFile(Loc.T("File"), () => im.FilePath, v => im.FilePath = v);
                EEnum(Loc.T("FitLabel"), () => im.Fit, v => im.Fit = v);
                EEnum(Loc.T("Dither"), () => im.Dither, v => im.Dither = v);
                EInt(Loc.T("Threshold"), () => im.Threshold, v => im.Threshold = v, 1, 254);
                break;
            case TableElement tb:
                EInt(Loc.T("Rows"), () => tb.Rows, v => tb.Rows = v, 1, 50);
                EInt(Loc.T("Columns"), () => tb.Columns, v => tb.Columns = v, 1, 50);
                EFloat(Loc.T("FontPt"), () => tb.FontSizePt, v => tb.FontSizePt = v, 4, 72, 1, 0);
                EFloatMm(Loc.T("Stroke"), () => tb.StrokeWidthMm, v => tb.StrokeWidthMm = v, 0, 10);
                ECells(tb);
                break;
        }
        _typeGrid.ResumeLayout();
    }

    // Commits a type-property change as one undoable command and repaints.
    private void CommitProp(string name, Action apply, Action revert)
    {
        _history.Execute(new DelegateCommand(name, apply, revert));
        _canvas.RefreshDocument();
    }

    private void EString(string label, Func<string> get, Action<string> set, bool multiline = false)
    {
        var tb = new TextBox { Dock = DockStyle.Fill, Multiline = multiline, Height = multiline ? 46 : 0 };
        tb.Text = get();
        string before = "";
        tb.Enter += (_, _) => before = get();
        tb.TextChanged += (_, _) => ApplyLive(() => set(tb.Text));   // live preview as you type
        tb.Validated += (_, _) => CommitChanged($"Edit {label}", before, get(), v => set(v), s => before = s);
        AddRow(_typeGrid, label, tb);
        _typeRefreshers.Add(() => tb.Text = get());
    }

    private void EInt(string label, Func<int> get, Action<int> set, int min, int max)
    {
        var n = new NumericUpDown { Dock = DockStyle.Fill, Minimum = min, Maximum = max, DecimalPlaces = 0 };
        n.Value = Math.Clamp(get(), min, max);
        int before = 0;
        n.Enter += (_, _) => before = get();
        n.ValueChanged += (_, _) => ApplyLive(() => set((int)n.Value));   // apply immediately
        n.Validated += (_, _) => CommitChanged($"Edit {label}", before, get(), v => set(v), v => before = v);
        AddRow(_typeGrid, label, n);
        _typeRefreshers.Add(() => n.Value = Math.Clamp(get(), min, max));
    }

    private void EFloat(string label, Func<float> get, Action<float> set, decimal min, decimal max, decimal inc, int dec)
    {
        var n = new NumericUpDown { Dock = DockStyle.Fill, Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = dec };
        n.Value = Math.Clamp((decimal)get(), min, max);
        float before = 0f;
        n.Enter += (_, _) => before = get();
        n.ValueChanged += (_, _) => ApplyLive(() => set((float)n.Value));   // apply immediately (e.g. font size)
        n.Validated += (_, _) => CommitChanged($"Edit {label}", before, get(), v => set(v), v => before = v);
        AddRow(_typeGrid, label, n);
        _typeRefreshers.Add(() => n.Value = Math.Clamp((decimal)get(), min, max));
    }

    // A length editor (stored in mm) shown/entered in the active unit, with a unit-suffixed label.
    private void EFloatMm(string label, Func<float> get, Action<float> set, decimal minMm, decimal maxMm)
    {
        int dec = UnitFormat.Decimals(_unit);
        decimal inc = UnitFormat.Increment(_unit);
        decimal min = ToDisplay((float)minMm), max = ToDisplay((float)maxMm);
        var n = new NumericUpDown { Dock = DockStyle.Fill, Minimum = min, Maximum = max, Increment = inc, DecimalPlaces = dec };
        n.Value = Math.Clamp(ToDisplay(get()), min, max);
        float before = 0f;
        n.Enter += (_, _) => before = get();
        n.ValueChanged += (_, _) => ApplyLive(() => set(FromDisplay(n.Value)));
        n.Validated += (_, _) => CommitChanged($"Edit {label}", before, get(), v => set(v), v => before = v);
        AddRow(_typeGrid, $"{label} ({UnitFormat.Suffix(_unit)})", n);
        _typeRefreshers.Add(() => n.Value = Math.Clamp(ToDisplay(get()), min, max));
    }

    // Applies a type-property change to the model and repaints, without touching the undo stack —
    // used for live editing; the single undo command is pushed on commit (focus-out).
    private void ApplyLive(Action apply)
    {
        if (_populating || _element is null) return;
        apply();
        _canvas.RefreshDocument();
    }

    // On focus-out, records the whole live edit as one undoable command (already applied to the model).
    private void CommitChanged<T>(string name, T before, T after, Action<T> set, Action<T> rebase)
        where T : IEquatable<T>
    {
        if (_populating || _element is null || after.Equals(before)) return;
        _history.PushExecuted(new DelegateCommand(name, () => set(after), () => set(before)));
        rebase(after);
    }

    private void EBool(string label, Func<bool> get, Action<bool> set)
    {
        var c = new CheckBox { Text = label, AutoSize = true, Checked = get() };
        c.CheckedChanged += (_, _) =>
        {
            if (_populating || c.Checked == get()) return;
            bool nv = c.Checked;
            CommitProp($"Toggle {label}", () => set(nv), () => set(!nv));
        };
        AddRow(_typeGrid, "", c);
        _typeRefreshers.Add(() => c.Checked = get());
    }

    private void EEnum<TEnum>(string label, Func<TEnum> get, Action<TEnum> set) where TEnum : struct, Enum
    {
        var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(Enum.GetNames<TEnum>().Cast<object>().ToArray());
        combo.SelectedItem = get().ToString();
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_populating || combo.SelectedItem is not string s) return;
            var nv = Enum.Parse<TEnum>(s);
            TEnum ov = get();
            if (nv.Equals(ov)) return;
            CommitProp($"Edit {label}", () => set(nv), () => set(ov));
        };
        AddRow(_typeGrid, label, combo);
        _typeRefreshers.Add(() => combo.SelectedItem = get().ToString());
    }

    private void EColor(string label, Func<Color> get, Action<Color> set)
    {
        var swatch = new Button { Dock = DockStyle.Fill, Height = 22, FlatStyle = FlatStyle.Flat, BackColor = get() };
        swatch.Click += (_, _) =>
        {
            using var dlg = new ColorDialog { Color = get(), FullOpen = true };
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Color.ToArgb() == get().ToArgb()) return;
            Color nv = dlg.Color, ov = get();
            CommitProp($"Edit {label}", () => set(nv), () => set(ov));
            swatch.BackColor = nv;
        };
        AddRow(_typeGrid, label, swatch);
        _typeRefreshers.Add(() => swatch.BackColor = get());
    }

    private void EFont(string label, Func<string> get, Action<string> set)
    {
        var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(FontFamily.Families.Select(f => f.Name).Distinct().Cast<object>().ToArray());
        combo.SelectedItem = get();
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_populating || combo.SelectedItem is not string nv || nv == get()) return;
            string ov = get();
            CommitProp($"Edit {label}", () => set(nv), () => set(ov));
        };
        AddRow(_typeGrid, label, combo);
        _typeRefreshers.Add(() => combo.SelectedItem = get());
    }

    private void EFile(string label, Func<string?> get, Action<string?> set)
    {
        var box = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = get() ?? "" };
        var browse = new Button { Text = "…", Dock = DockStyle.Fill, Height = 22 };
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Images|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            string? nv = dlg.FileName, ov = get();
            CommitProp($"Set {label}", () => set(nv), () => set(ov));
            box.Text = nv ?? "";
        };
        AddRow(_typeGrid, label, box);
        AddRow(_typeGrid, "", browse);
        _typeRefreshers.Add(() => box.Text = get() ?? "");
    }

    // QR centre-logo editor: embeds the chosen image's bytes (portable) from a file or the bundled
    // clip-art library, + a Clear; warns when the logo is too large for the error-correction budget.
    private void ELogo(QrElement q)
    {
        var status = new Label { AutoSize = true, ForeColor = Color.Gray };
        var fromFile = new Button { Text = Loc.T("LogoFile"), Dock = DockStyle.Fill, Height = 22 };
        var fromClipart = new Button { Text = Loc.T("LogoClipart"), Dock = DockStyle.Fill, Height = 22 };
        var clear = new Button { Text = Loc.T("ClearLogo"), Dock = DockStyle.Fill, Height = 22 };

        void Refresh()
        {
            clear.Enabled = q.LogoData is not null;
            status.Text = q.LogoData is null ? Loc.T("LogoNone")
                : q.LogoExceedsBudget ? Loc.T("LogoTooLarge") : Loc.T("LogoEmbedded");
            status.ForeColor = q.LogoExceedsBudget ? Color.FromArgb(176, 0, 32) : Color.Gray;
        }

        // Embeds the bytes of an image file as the logo (undoable).
        void SetLogo(string path)
        {
            byte[]? nv;
            try { nv = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
            byte[]? ov = q.LogoData;
            CommitProp("Set QR logo", () => q.LogoData = nv, () => q.LogoData = ov);
            Refresh();
        }

        fromFile.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Images|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) SetLogo(dlg.FileName);
        };
        fromClipart.Click += (_, _) =>
        {
            using var picker = new ClipartPicker();
            if (picker.ShowDialog(this) == DialogResult.OK && picker.SelectedPath is { } path) SetLogo(path);
        };
        clear.Click += (_, _) =>
        {
            byte[]? ov = q.LogoData;
            if (ov is null) return;
            CommitProp("Clear QR logo", () => q.LogoData = null, () => q.LogoData = ov);
            Refresh();
        };

        AddRow(_typeGrid, Loc.T("Logo"), fromFile);
        AddRow(_typeGrid, "", fromClipart);
        AddRow(_typeGrid, "", clear);
        AddRow(_typeGrid, "", status);
        _typeRefreshers.Add(Refresh);
    }

    private void ECells(TableElement t)
    {
        var tb = new TextBox { Dock = DockStyle.Fill, Multiline = true, Height = 70, ScrollBars = ScrollBars.Vertical };
        tb.Text = CellsToText(t);
        List<string> before = [];
        tb.Enter += (_, _) => before = [.. t.Cells];
        tb.TextChanged += (_, _) => ApplyLive(() => t.Cells = ParseCells(tb.Text, t.Rows, t.Columns));
        tb.Validated += (_, _) =>
        {
            if (_populating) return;
            List<string> nv = [.. t.Cells], ov = before;
            if (nv.SequenceEqual(ov)) return;
            _history.PushExecuted(new DelegateCommand("Edit cells", () => t.Cells = [.. nv], () => t.Cells = [.. ov]));
            before = nv;
        };
        AddRow(_typeGrid, Loc.T("Cells"), tb);
        AddRow(_typeGrid, "", new Label { Text = Loc.T("CellsHint"), AutoSize = true, ForeColor = Color.Gray });
        _typeRefreshers.Add(() => tb.Text = CellsToText(t));
    }

    private static string CellsToText(TableElement t)
    {
        var lines = new List<string>();
        for (int r = 0; r < t.Rows; r++)
        {
            var cells = new List<string>();
            for (int c = 0; c < t.Columns; c++)
            {
                int i = r * t.Columns + c;
                cells.Add(i < t.Cells.Count ? t.Cells[i] : "");
            }
            lines.Add(string.Join(", ", cells));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static List<string> ParseCells(string text, int rows, int cols)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        var flat = new List<string>(rows * cols);
        for (int r = 0; r < rows; r++)
        {
            string[] parts = r < lines.Length ? lines[r].Split(',') : [];
            for (int c = 0; c < cols; c++)
                flat.Add(c < parts.Length ? parts[c].Trim() : "");
        }
        return flat;
    }
}
