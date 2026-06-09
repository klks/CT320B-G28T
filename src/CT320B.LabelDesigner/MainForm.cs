using CT320B.LabelDesigner.Controls;
using CT320B.LabelDesigner.Core.Editing;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Model.Elements;
using CT320B.LabelDesigner.Core.Printing;
using CT320B.LabelDesigner.Core.VariableData;
using CT320B.LabelDesigner.Services;
using CT320B.UsbApi;

namespace CT320B.LabelDesigner;

/// <summary>
/// Application shell: an Office-style <see cref="RibbonControl"/> (Home / Templates) on top, a left
/// insert bar, a bottom device bar, and a centre that hosts multiple document tabs
/// (<see cref="EditorTabs"/>) — each a self-contained <see cref="LabelEditor"/> with its own canvas,
/// undo stack, and Properties/Layers inspector. The Templates ribbon tab swaps the centre for an
/// in-frame <see cref="TemplateBrowser"/>; double-clicking a template opens a new tab. The shared ribbon
/// and shortcuts act on the active tab.
/// </summary>
public sealed class MainForm : Form
{
    /// <summary>The shared printer connection, reused by every panel.</summary>
    public PrinterService PrinterService { get; }

    private readonly Label _zoomLabel = new() { Text = "100%", AutoSize = true, Margin = new Padding(2, 8, 2, 2) };
    private readonly NumericUpDown _copies = new() { Minimum = 1, Maximum = 99, Value = 1, Width = 52 };
    private readonly NumericUpDown _offsetX = new() { Minimum = -20, Maximum = 20, DecimalPlaces = 1, Increment = 0.5m, Width = 54 };
    private readonly NumericUpDown _offsetY = new() { Minimum = -20, Maximum = 20, DecimalPlaces = 1, Increment = 0.5m, Width = 54 };
    private Button _undoBtn = null!, _redoBtn = null!, _printBtn = null!;
    private readonly TemplateLibrary _library = new();
    private DeviceStatusBar _statusBar = null!;
    private RibbonControl _ribbon = null!;
    private EditorTabs _editorTabs = null!;
    private TemplateBrowser _browser = null!;
    private AboutView _about = null!;
    private ContextMenuStrip _contextMenu = null!;
    private LogPanel _logPanel = null!;
    private ToastHost _toasts = null!;
    private AutoSaveService _autoSave = null!;
    private readonly AppSettings _settings = AppSettings.Load();
    private CheckBox[] _viewChecks = [];
    private (float X, float Y) _offset;   // current per-printer print offset (mm)
    private bool _syncingOffsets;   // suppresses the offset spinners' write-back during a refresh

    // The shared ribbon / insert bar / shortcuts act on the active document tab.
    private LabelEditor ActiveEditor => _editorTabs.ActiveEditor!;
    private CanvasControl _canvas => ActiveEditor.Canvas;
    private UndoStack _history => ActiveEditor.History;

    public MainForm()
    {
        Text = Loc.T("AppTitle");
        MinimumSize = new Size(900, 620);
        RestoreWindow();
        PrinterService = new PrinterService(new AppLoggerProvider().CreateLogger("Printer"));
        _offset = _settings.OffsetFor(PrinterService.ConnectedDescription);

        _statusBar = new DeviceStatusBar(PrinterService);
        _statusBar.LanguageSelected += ChangeLanguage;   // bottom-left language picker
        _contextMenu = BuildContextMenu();   // shared by every editor's canvas (acts on the active one)

        // Phase 14a: non-modal log/toast surface.
        _logPanel = new LogPanel { Visible = _settings.ShowLog };
        _logPanel.HideRequested += () => SetLogVisible(false);
        _toasts = new ToastHost();
        AppLog.EntryAdded += OnAppLogEntry;

        _browser = new TemplateBrowser(_library) { Dock = DockStyle.Fill, Visible = false };
        _browser.Opened += (doc, path) => OpenInTab(doc, path);
        _about = new AboutView { Dock = DockStyle.Fill, Visible = false };   // cracktro shown under the About tab

        _editorTabs = new EditorTabs { Dock = DockStyle.Fill };
        _editorTabs.ActiveChanged += (_, _) => OnActiveEditorChanged();
        _editorTabs.EditorClosing += (_, e) => { if (!ConfirmSave(e.Editor)) e.Cancel = true; };

        var centre = new Panel { Dock = DockStyle.Fill };
        centre.Controls.Add(_about);         // overlay (shown under the About ribbon tab)
        centre.Controls.Add(_browser);       // overlay (shown under the Templates ribbon tab)
        centre.Controls.Add(_editorTabs);    // document tabs

        _ribbon = BuildRibbon();

        Controls.Add(centre);                                          // fill
        Controls.Add(_logPanel);                                      // bottom (above the device bar)
        Controls.Add(new Splitter { Dock = DockStyle.Left, Width = 4 });
        Controls.Add(BuildInsertBar());                               // left: insert tools
        Controls.Add(_statusBar);                                     // bottom: printer (task area)
        Controls.Add(_ribbon);                                        // top

        Controls.Add(_toasts);                                        // floating overlay (bottom-right)
        _toasts.BringToFront();
        Resize += (_, _) => _toasts.Reposition();

        // Pause the About cracktro (and release its bitmap) when the window loses focus; resume on return.
        Deactivate += (_, _) => _about.Running = false;
        Activated += (_, _) => _about.Running = _ribbon.SelectedTabName == "About";

        PrinterService.StatusChanged += OnPrinterStatusChanged;       // apply per-printer offset on connect
        PrinterService.ErrorOccurred += AppLog.Error;                 // surface connect errors as toasts + log

        // Phase 14b: crash recovery + periodic autosave of dirty tabs.
        _autoSave = new AutoSaveService(() => _editorTabs.Editors
            .Select(e => new AutoSaveService.TabSnapshot(e.RecoveryId, e.FilePath, e.Title, e.Dirty, e.Document))
            .ToList());
        if (!TryRestoreRecovery())
            OpenInTab(SampleDocuments.Starter(), null);   // start with one document open
        _autoSave.Start();
    }

    // Offers any snapshots a previous crash left behind; restores them as dirty tabs. Returns true if a
    // tab was opened (so the caller skips the blank starter document).
    private bool TryRestoreRecovery()
    {
        IReadOnlyList<RecoveredDocument> recovered = AutoSaveService.LoadRecovery();
        if (recovered.Count == 0) return false;

        DialogResult answer = MessageBox.Show(this,
            Loc.F("RecoverPrompt", recovered.Count),
            Loc.T("RecoverTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) { AutoSaveService.ClearAll(); return false; }

        foreach (RecoveredDocument r in recovered)
        {
            LabelEditor editor = OpenInTab(r.Document, r.OriginalPath);
            editor.MarkModified();   // recovered work is unsaved until the user saves it
        }
        AutoSaveService.ClearAll();   // the timer writes fresh snapshots from here on
        AppLog.Info(Loc.F("Recovered", recovered.Count));
        return true;
    }

    // Routes log entries to the toast overlay (everything but plain Info, to avoid noise) on the UI thread.
    private void OnAppLogEntry(LogEntry entry)
    {
        if (entry.Severity == LogSeverity.Info) return;
        if (IsHandleCreated && InvokeRequired) BeginInvoke(() => _toasts.Show(entry.Severity, entry.Message));
        else if (IsHandleCreated) _toasts.Show(entry.Severity, entry.Message);
    }

    private void SetLogVisible(bool show)
    {
        _logPanel.Visible = show;
        _settings.ShowLog = show;
        if (_viewChecks.Length > 3) _viewChecks[3].Checked = show;
    }

    // Phase 14d: switch the ruler / setup-dialog measurement unit across all open tabs.
    private void SetInches(bool inches)
    {
        Core.Model.MeasurementUnit unit = inches ? Core.Model.MeasurementUnit.Inches : Core.Model.MeasurementUnit.Millimeters;
        _settings.Unit = unit;
        foreach (LabelEditor editor in _editorTabs.Editors) editor.Unit = unit;
    }

    // --- persisted window state & per-printer calibration (Phase 9) ---
    private void RestoreWindow()
    {
        Size = new Size(Math.Max(MinimumSize.Width, _settings.WindowWidth),
                        Math.Max(MinimumSize.Height, _settings.WindowHeight));
        if (_settings.WindowX > int.MinValue &&
            SystemInformation.VirtualScreen.Contains(new Rectangle(_settings.WindowX, _settings.WindowY, 60, 60)))
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_settings.WindowX, _settings.WindowY);
        }
        else StartPosition = FormStartPosition.CenterScreen;
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void SaveSettings()
    {
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = b.X; _settings.WindowY = b.Y;
        _settings.WindowWidth = b.Width; _settings.WindowHeight = b.Height;
        _settings.Save();
    }

    private void OnPrinterStatusChanged(ConnectionStatus status)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(() => OnPrinterStatus(status));
        else OnPrinterStatus(status);
    }

    private void OnPrinterStatus(ConnectionStatus status)
    {
        if (status != ConnectionStatus.Connected) return;
        string? key = PrinterService.ConnectedDescription;
        (float x, float y) = _settings.OffsetFor(key);
        SetOffset(x, y, save: false);
        _settings.LastDeviceKey = key;
        AppLog.Success(Loc.F("ConnectedTo", key ?? ""));
    }

    // Sets the print offset on every open document + the ribbon spinners; persists per printer when asked.
    private void SetOffset(float x, float y, bool save)
    {
        _offset = (x, y);
        foreach (LabelEditor ed in _editorTabs.Editors)
        { ed.Document.PrintOffsetXMm = x; ed.Document.PrintOffsetYMm = y; }
        _syncingOffsets = true;
        _offsetX.Value = (decimal)Math.Clamp(x, (float)_offsetX.Minimum, (float)_offsetX.Maximum);
        _offsetY.Value = (decimal)Math.Clamp(y, (float)_offsetY.Minimum, (float)_offsetY.Maximum);
        _syncingOffsets = false;
        if (save) _settings.SetOffset(PrinterService.ConnectedDescription, x, y);
    }

    private Control BuildInsertBar()
    {
        var bar = new InsertBar();
        bar.AddItem(Loc.T("Text"), RibbonIcons.Icon("text", 20), () => InsertElement(NewText()));
        bar.AddItem(Loc.T("Box"), RibbonIcons.Icon("box", 20), () => InsertElement(NewShape(ShapeKind.Box)));
        bar.AddItem(Loc.T("Ellipse"), RibbonIcons.Icon("circle", 20), () => InsertElement(NewShape(ShapeKind.Ellipse)));
        bar.AddItem(Loc.T("Line"), RibbonIcons.Icon("line", 20), () => InsertElement(NewLine()));
        bar.AddItem(Loc.T("Rounded"), RibbonIcons.Icon("rounded", 20), () => InsertElement(NewShape(ShapeKind.RoundRect)));
        bar.AddItem(Loc.T("Triangle"), ShapeIcon(ShapeKind.Triangle), () => InsertElement(NewShape(ShapeKind.Triangle)));
        bar.AddItem(Loc.T("Diamond"), ShapeIcon(ShapeKind.Diamond), () => InsertElement(NewShape(ShapeKind.Diamond)));
        bar.AddItem(Loc.T("Polygon"), ShapeIcon(ShapeKind.Polygon), () => InsertElement(NewShape(ShapeKind.Polygon)));
        bar.AddItem(Loc.T("Star"), ShapeIcon(ShapeKind.Star), () => InsertElement(NewShape(ShapeKind.Star)));
        bar.AddItem(Loc.T("Arrow"), ShapeIcon(ShapeKind.Arrow), () => InsertElement(NewShape(ShapeKind.Arrow)));
        bar.AddSeparator();
        bar.AddItem(Loc.T("QrCode"), RibbonIcons.Icon("qr", 20), () => InsertElement(NewQr()));
        bar.AddItem(Loc.T("Barcode"), RibbonIcons.Icon("barcode", 20), () => InsertElement(NewBarcode()));
        bar.AddSeparator();
        bar.AddItem(Loc.T("Image"), RibbonIcons.Icon("image", 20), InsertImage);
        bar.AddItem(Loc.T("Clipart"), RibbonIcons.Icon("clipart", 20), InsertClipart);
        bar.AddItem(Loc.T("Table"), RibbonIcons.Icon("table", 20), () => InsertElement(NewTable()));
        return bar;
    }

    // --- ribbon ---
    private RibbonControl BuildRibbon()
    {
        var ribbon = new RibbonControl();

        // Home holds everything: File | Clipboard | Editing | Print | Printer | Order | Arrange | View
        RibbonTab home = ribbon.AddTab("Home", Loc.T("TabHome"));

        RibbonGroup file = home.AddGroup(Loc.T("GrpFile"), "open", collapsePriority: 80);
        file.AddLarge(Loc.T("New"), "new", NewLabel);
        file.AddLarge(Loc.T("Open"), "open", OpenLabel);
        file.AddLargeMenu(Loc.T("Recent"), "open", RecentMenu());
        file.AddSmallColumn(
            (Loc.T("Save"), "save", () => SaveLabel()),
            (Loc.T("SaveAs"), "saveas", () => SaveLabelAs()),
            (Loc.T("ExportPng"), "image", ExportPng));

        RibbonGroup clip = home.AddGroup(Loc.T("GrpClipboard"), "paste", collapsePriority: 60);
        clip.AddLarge(Loc.T("Paste"), "paste", () => _canvas.Paste());
        clip.AddSmallColumn(
            (Loc.T("Cut"), "cut", () => _canvas.CutSelection()),
            (Loc.T("Copy"), "copy", () => _canvas.CopySelection()),
            (Loc.T("Duplicate"), "duplicate", () => _canvas.DuplicateSelection()));

        RibbonGroup editing = home.AddGroup(Loc.T("GrpEditing"), "undo", collapsePriority: 70);
        Button[] ur = editing.AddSmallColumn(
            (Loc.T("Undo"), "undo", () => _history.Undo()),
            (Loc.T("Redo"), "redo", () => _history.Redo()),
            (Loc.T("Delete"), "delete", () => _canvas.DeleteSelection()));
        _undoBtn = ur[0];
        _redoBtn = ur[1];

        RibbonGroup print = home.AddGroup(Loc.T("GrpPrint"), "print", collapsePriority: 90);
        _printBtn = print.AddLarge(Loc.T("Print"), "print", PrintLabel, RibbonIcons.Accent);
        print.AddLarge(Loc.T("Preview"), "preview", ShowPrintPreview);
        print.AddLarge(Loc.T("Batch"), "table", ShowBatchPrint);
        print.AddControl(CopiesControl());
        print.AddControl(OffsetControl());

        RibbonGroup printer = home.AddGroup(Loc.T("GrpPrinter"), "wrench", collapsePriority: 20);
        printer.AddLarge(Loc.T("Control"), "wrench", ShowControlPanel);

        RibbonGroup order = home.AddGroup(Loc.T("GrpOrder"), "front", collapsePriority: 40);
        order.AddSmallColumn(
            (Loc.T("BringToFront"), "front", () => _canvas.BringSelectionToFront()),
            (Loc.T("SendToBack"), "back", () => _canvas.SendSelectionToBack()),
            (Loc.T("LockUnlock"), "lock", () => _canvas.ToggleLockSelection()));

        RibbonGroup arrange = home.AddGroup(Loc.T("GrpArrange"), "align", collapsePriority: 50);
        arrange.AddLargeMenu(Loc.T("Align"), "align", AlignMenu());
        arrange.AddLargeMenu(Loc.T("Distribute"), "distribute", DistributeMenu());
        arrange.AddLargeMenu(Loc.T("Rotate"), "rotate", RotateMenu());
        arrange.AddLargeMenu(Loc.T("Flip"), "flip", FlipMenu());

        RibbonGroup label = home.AddGroup(Loc.T("GrpLabel"), "ruler", collapsePriority: 30);
        label.AddLarge(Loc.T("Setup"), "ruler", ShowLabelSetup);
        label.AddLarge(Loc.T("RotateView"), "rotate", () => _canvas.RotateView(90));

        RibbonGroup view = home.AddGroup(Loc.T("GrpView"), "fit", collapsePriority: 10);
        view.AddLarge(Loc.T("Fit"), "fit", () => _canvas.ZoomToFit());
        view.AddLarge(Loc.T("Hundred"), "ratio", () => _canvas.ZoomToHundred());
        _viewChecks = view.AddCheckColumn(
            (Loc.T("Grid"), _settings.ShowGrid, on => { _canvas.ShowGrid = on; _canvas.RefreshDocument(); _settings.ShowGrid = on; }),
            (Loc.T("Snap"), _settings.SnapToGrid, on => { _canvas.SnapToGrid = on; _settings.SnapToGrid = on; }),
            (Loc.T("SafeMargin"), _settings.ShowSafeMargin, on => { _canvas.ShowSafeMargin = on; _canvas.RefreshDocument(); _settings.ShowSafeMargin = on; }),
            (Loc.T("Log"), _settings.ShowLog, SetLogVisible),
            (Loc.T("Inches"), _settings.Unit == Core.Model.MeasurementUnit.Inches, SetInches));
        view.AddControl(_zoomLabel);

        // Templates tab — selecting it shows the in-frame browser; double-click there opens a new tab.
        RibbonTab templates = ribbon.AddTab("Templates", Loc.T("TabTemplates"));
        RibbonGroup tpl = templates.AddGroup(Loc.T("GrpTemplates"));
        tpl.AddLarge(Loc.T("NewBlank"), "new", () => OpenInTab(BlankDocument(), null));
        tpl.AddLarge(Loc.T("OpenFile"), "open", OpenLabel);
        tpl.AddLarge(Loc.T("Refresh"), "redo", () => _browser.Reload());
        tpl.AddLarge(Loc.T("MyTemplates"), "open", OpenMyTemplatesFolder);

        // About tab — selecting it shows the cracktro; a button returns to the design.
        RibbonTab about = ribbon.AddTab("About", Loc.T("TabAbout"));
        RibbonGroup ab = about.AddGroup(Loc.T("TabAbout"), "preview");
        ab.AddLarge(Loc.T("AboutBack"), "back", () => _ribbon.SelectTab("Home"));

        ribbon.SelectedTabChanged += (_, _) => OnRibbonTabChanged();
        return ribbon;
    }

    // Opens the user-writable templates drop folder in Explorer; files dropped there appear on Refresh.
    private void OpenMyTemplatesFolder()
    {
        AppPaths.EnsureDirectories();
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.MyTemplatesDir) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(Loc.T("OpenFolderErr"), ex); }
    }

    // The Templates ribbon tab shows the in-frame template browser; any other tab shows the documents.
    private void OnRibbonTabChanged()
    {
        string? tab = _ribbon.SelectedTabName;
        bool showBrowser = tab == "Templates";
        bool showAbout = tab == "About";
        if (showBrowser) { _browser.Reload(); _browser.BringToFront(); }
        _browser.Visible = showBrowser;
        _about.Visible = showAbout;
        _about.Running = showAbout;            // animate only while the About tab is open
        if (showAbout) _about.BringToFront();
    }

    private static LabelDocument BlankDocument() =>
        new() { Name = Loc.T("Untitled"), WidthMm = 30, HeightMm = 40, PrintOffsetXMm = -1f, PrintOffsetYMm = -1f };

    // Opens a document in a new tab (applying the current calibration + view prefs) and switches to it.
    private LabelEditor OpenInTab(LabelDocument document, string? path)
    {
        document.PrintOffsetXMm = _offset.X;   // per-printer calibration applies to every label
        document.PrintOffsetYMm = _offset.Y;
        LabelEditor editor = _editorTabs.AddEditor(document, path);
        editor.Canvas.ContextMenuStrip = _contextMenu;
        editor.Canvas.ShowGrid = _settings.ShowGrid;
        editor.Canvas.SnapToGrid = _settings.SnapToGrid;
        editor.Canvas.ShowSafeMargin = _settings.ShowSafeMargin;
        editor.Unit = _settings.Unit;
        editor.Canvas.RefreshDocument();
        _ribbon.SelectTab("Home");
        _browser.Visible = false;

        // Pull in any URL-only images (e.g. an imported .ddl pre-printed background) in the background;
        // the canvas shows placeholders until each download finishes, then repaints.
        _ = ImageResolver.ResolveAsync(document, () =>
        {
            if (editor.Canvas.IsHandleCreated && !editor.IsDisposed) editor.Canvas.RefreshDocument();
        });
        return editor;
    }

    private void OnActiveEditorChanged()
    {
        if (_editorTabs.ActiveEditor is null) { OpenInTab(BlankDocument(), null); return; }   // never zero tabs
        RefreshFromActive();
    }

    private void RefreshFromActive()
    {
        LabelEditor ed = ActiveEditor;
        _undoBtn.Enabled = ed.History.CanUndo;
        _redoBtn.Enabled = ed.History.CanRedo;
        _zoomLabel.Text = $"{ed.Canvas.Zoom / 8f * 100f:0}%";
        _syncingOffsets = true;
        _offsetX.Value = (decimal)ed.Document.PrintOffsetXMm;
        _offsetY.Value = (decimal)ed.Document.PrintOffsetYMm;
        _syncingOffsets = false;
        _statusBar.Info = Loc.F("StatusLabelSize", ed.Document.WidthMm.ToString("0.#"), ed.Document.HeightMm.ToString("0.#"));
        Text = $"{(ed.Dirty ? "*" : "")}{ed.Title} — {Loc.T("AppTitle")}";
    }

    // Adds an element to the active document; if the Templates browser is showing, switch back to the
    // Home view first so the inserted element is visible.
    private void InsertElement(LabelElement element)
    {
        _ribbon.SelectTab("Home");
        _canvas.AddElement(element);
    }

    private void InsertImage()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Insert image",
            Filter = "Images|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files|*.*",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            InsertElement(new ImageElement { Name = "Image", FilePath = dlg.FileName, WidthMm = 20, HeightMm = 20 });
    }

    // Bundled clip-art / emoji: embed the chosen PNG so the label stays self-contained.
    private void InsertClipart()
    {
        using var dlg = new ClipartPicker();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedPath is not { } path) return;
        try
        {
            byte[] data = File.ReadAllBytes(path);
            InsertElement(new ImageElement { Name = "Clip-art", ImageData = data, WidthMm = 15, HeightMm = 15 });
        }
        catch (Exception ex) { ShowError(Loc.T("InsertClipart"), ex); }
    }

    private static TextElement NewText() => new()
    { Name = "Text", Text = "Text", FontSizePt = 10, WidthMm = 24, HeightMm = 7, Alignment = TextAlignment.Center };

    private static ShapeElement NewShape(ShapeKind kind) => new()
    { Name = kind.ToString(), Kind = kind, WidthMm = 18, HeightMm = 12, StrokeWidthMm = 0.4f, CornerRadiusMm = 2f };

    // Renders a shape kind to a small monochrome icon, so an insert button always matches its shape.
    private static Image ShapeIcon(ShapeKind kind)
    {
        var bmp = new Bitmap(20, 20);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            new ShapeElement
            {
                Kind = kind, XMm = 2, YMm = 3, WidthMm = 16, HeightMm = 14,
                StrokeColor = RibbonIcons.Ink, FillColor = RibbonIcons.Ink, StrokeWidthMm = 1.4f,
            }.Render(g, Core.Rendering.RenderContext.ForScreen(1));
        }
        return bmp;
    }

    private static ShapeElement NewLine() => new()
    { Name = "Line", Kind = ShapeKind.Line, WidthMm = 24, HeightMm = 6, StrokeWidthMm = 0.4f };

    private static QrElement NewQr() => new()
    { Name = "QR", Data = "https://example.com", WidthMm = 20, HeightMm = 20 };

    private static BarcodeElement NewBarcode() => new()
    { Name = "Barcode", Data = "12345678", Symbology = BarcodeSymbology.Code128, ShowText = true, WidthMm = 32, HeightMm = 14 };

    private static TableElement NewTable() => new()
    { Name = "Table", Rows = 2, Columns = 2, Cells = ["", "", "", ""], WidthMm = 30, HeightMm = 16 };

    // Print-offset calibration (mm): nudges the whole printout to fix a printer that lands content
    // off-origin (e.g. set Y to -1 when content prints ~1 mm too low and clips at the bottom).
    private Control OffsetControl()
    {
        var tip = new ToolTip();
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(2) };
        grid.Controls.Add(new Label { Text = Loc.T("OffX"), AutoSize = true, Margin = new Padding(0, 5, 3, 0) }, 0, 0);
        grid.Controls.Add(_offsetX, 1, 0);
        grid.Controls.Add(new Label { Text = Loc.T("OffY"), AutoSize = true, Margin = new Padding(0, 5, 3, 0) }, 0, 1);
        grid.Controls.Add(_offsetY, 1, 1);

        // Print offset is a per-printer calibration (persisted, applied to every label) — not document
        // content; editing it doesn't dirty the document. The guard skips write-back during a refresh.
        _offsetX.ValueChanged += (_, _) => OnOffsetChanged();
        _offsetY.ValueChanged += (_, _) => OnOffsetChanged();
        tip.SetToolTip(_offsetY, Loc.T("PrintOffsetTip"));
        return grid;
    }

    private void OnOffsetChanged()
    {
        if (_syncingOffsets) return;
        SetOffset((float)_offsetX.Value, (float)_offsetY.Value, save: true);
    }

    private Control CopiesControl()
    {
        var panel = new FlowLayoutPanel
        { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(2) };
        panel.Controls.Add(new Label { Text = Loc.T("Copies"), AutoSize = true, Margin = new Padding(0, 6, 0, 2), Font = new Font("Segoe UI", 8f) });
        panel.Controls.Add(_copies);
        return panel;
    }

    // Language picker (in the bottom device bar): choosing a language saves it and prompts to restart.
    private void ChangeLanguage(string code)
    {
        if (code == _settings.LanguageCode) return;
        _settings.LanguageCode = code;
        _settings.Save();
        MessageBox.Show(this, Loc.T("LangRestart"), Loc.T("Language"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private ContextMenuStrip AlignMenu()
    {
        var m = new ContextMenuStrip();
        AddTransform(m.Items, Loc.T("Left"), s => LabelTransforms.Align(s, AlignKind.Left));
        AddTransform(m.Items, Loc.T("Center"), s => LabelTransforms.Align(s, AlignKind.HCenter));
        AddTransform(m.Items, Loc.T("Right"), s => LabelTransforms.Align(s, AlignKind.Right));
        m.Items.Add(new ToolStripSeparator());
        AddTransform(m.Items, Loc.T("Top"), s => LabelTransforms.Align(s, AlignKind.Top));
        AddTransform(m.Items, Loc.T("Middle"), s => LabelTransforms.Align(s, AlignKind.VMiddle));
        AddTransform(m.Items, Loc.T("Bottom"), s => LabelTransforms.Align(s, AlignKind.Bottom));
        return m;
    }

    private ContextMenuStrip DistributeMenu()
    {
        var m = new ContextMenuStrip();
        AddTransform(m.Items, Loc.T("Horizontally"), s => LabelTransforms.Distribute(s, true));
        AddTransform(m.Items, Loc.T("Vertically"), s => LabelTransforms.Distribute(s, false));
        return m;
    }

    private ContextMenuStrip RotateMenu()
    {
        var m = new ContextMenuStrip();
        AddTransform(m.Items, Loc.T("Rot90"), s => LabelTransforms.Rotate(s, 90));
        AddTransform(m.Items, Loc.T("Rot180"), s => LabelTransforms.Rotate(s, 180));
        AddTransform(m.Items, Loc.T("Rot270"), s => LabelTransforms.Rotate(s, 270));
        return m;
    }

    private ContextMenuStrip FlipMenu()
    {
        var m = new ContextMenuStrip();
        AddTransform(m.Items, Loc.T("Horizontal"), s => LabelTransforms.Flip(s, true));
        AddTransform(m.Items, Loc.T("Vertical"), s => LabelTransforms.Flip(s, false));
        return m;
    }

    // --- right-click context menu ---
    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        ToolStripMenuItem cut = Item(menu, Loc.T("Cut"), () => _canvas.CutSelection());
        ToolStripMenuItem copy = Item(menu, Loc.T("Copy"), () => _canvas.CopySelection());
        ToolStripMenuItem paste = Item(menu, Loc.T("Paste"), () => _canvas.Paste());
        ToolStripMenuItem dup = Item(menu, Loc.T("Duplicate"), () => _canvas.DuplicateSelection());
        ToolStripMenuItem del = Item(menu, Loc.T("Delete"), () => _canvas.DeleteSelection());
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem front = Item(menu, Loc.T("BringToFront"), () => _canvas.BringSelectionToFront());
        ToolStripMenuItem back = Item(menu, Loc.T("SendToBack"), () => _canvas.SendSelectionToBack());
        ToolStripMenuItem lockItem = Item(menu, Loc.T("LockUnlock"), () => _canvas.ToggleLockSelection());
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem group = Item(menu, Loc.T("Group"), () => _canvas.GroupSelection());
        ToolStripMenuItem ungroup = Item(menu, Loc.T("Ungroup"), () => _canvas.UngroupSelection());
        menu.Items.Add(new ToolStripSeparator());

        var alignSub = new ToolStripMenuItem(Loc.T("Align"));
        AddTransform(alignSub.DropDownItems, Loc.T("Left"), s => LabelTransforms.Align(s, AlignKind.Left));
        AddTransform(alignSub.DropDownItems, Loc.T("Center"), s => LabelTransforms.Align(s, AlignKind.HCenter));
        AddTransform(alignSub.DropDownItems, Loc.T("Right"), s => LabelTransforms.Align(s, AlignKind.Right));
        AddTransform(alignSub.DropDownItems, Loc.T("Top"), s => LabelTransforms.Align(s, AlignKind.Top));
        AddTransform(alignSub.DropDownItems, Loc.T("Middle"), s => LabelTransforms.Align(s, AlignKind.VMiddle));
        AddTransform(alignSub.DropDownItems, Loc.T("Bottom"), s => LabelTransforms.Align(s, AlignKind.Bottom));
        var rotSub = new ToolStripMenuItem(Loc.T("Rotate"));
        AddTransform(rotSub.DropDownItems, Loc.T("Rot90"), s => LabelTransforms.Rotate(s, 90));
        AddTransform(rotSub.DropDownItems, Loc.T("Rot180"), s => LabelTransforms.Rotate(s, 180));
        AddTransform(rotSub.DropDownItems, Loc.T("Rot270"), s => LabelTransforms.Rotate(s, 270));
        var flipSub = new ToolStripMenuItem(Loc.T("Flip"));
        AddTransform(flipSub.DropDownItems, Loc.T("Horizontal"), s => LabelTransforms.Flip(s, true));
        AddTransform(flipSub.DropDownItems, Loc.T("Vertical"), s => LabelTransforms.Flip(s, false));
        menu.Items.Add(alignSub);
        menu.Items.Add(rotSub);
        menu.Items.Add(flipSub);
        menu.Items.Add(new ToolStripSeparator());
        Item(menu, Loc.T("SelectAll"), () => _canvas.SelectAll());
        Item(menu, Loc.T("Print"), PrintLabel);
        Item(menu, Loc.T("PrintPreviewMenu"), ShowPrintPreview);
        Item(menu, Loc.T("BatchVarMenu"), ShowBatchPrint);

        menu.Opening += (_, _) =>
        {
            bool has = _canvas.Selection.Count > 0;
            bool multi = _canvas.Selection.Count > 1;
            cut.Enabled = copy.Enabled = dup.Enabled = del.Enabled = front.Enabled = back.Enabled =
                lockItem.Enabled = has;
            paste.Enabled = _canvas.CanPaste;
            alignSub.Enabled = multi;
            rotSub.Enabled = flipSub.Enabled = has;
            group.Enabled = multi;
            ungroup.Enabled = _canvas.SelectionHasGroup;
        };
        return menu;
    }

    private ToolStripMenuItem Item(ContextMenuStrip menu, string text, Action onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => onClick();
        menu.Items.Add(item);
        return item;
    }

    private void AddTransform(ToolStripItemCollection items, string text,
        Func<IReadOnlyList<LabelElement>, GeometryCommand?> build)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            GeometryCommand? cmd = build(_canvas.Selection);
            if (cmd is not null) { _history.Execute(cmd); _canvas.RefreshDocument(); }
        };
        items.Add(item);
    }

    // --- label setup (width/height/gap) ---
    private void ShowLabelSetup()
    {
        LabelDocument doc = _canvas.Document;
        using var dlg = new LabelSetupForm(doc, _settings.Unit);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (doc.WidthMm == dlg.WidthMm && doc.HeightMm == dlg.HeightMm && doc.GapMm == dlg.GapMm) return;

        doc.WidthMm = dlg.WidthMm;
        doc.HeightMm = dlg.HeightMm;
        doc.GapMm = dlg.GapMm;
        _canvas.ZoomToFit();          // re-fit the new page size
        _canvas.RefreshDocument();
        ActiveEditor.MarkModified();
        RefreshFromActive();
    }

    // --- file: each operation acts on / opens a document tab ---
    // "New" opens a blank document tab directly; pick a template from the Templates ribbon tab.
    private void NewLabel() => OpenInTab(BlankDocument(), null);

    private void OpenLabel()
    {
        using var dlg = new OpenFileDialog { Filter = AppPaths.FileFilter, InitialDirectory = AppPaths.LabelsDir };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        OpenPath(dlg.FileName);
    }

    private void OpenRecent(string path) => OpenPath(path);

    private void OpenPath(string path)
    {
        try { OpenInTab(_library.Open(path), path); }
        catch (Exception ex) { ShowError(Loc.T("Open"), ex); }
    }

    private bool SaveLabel() => SaveEditor(ActiveEditor);
    private bool SaveLabelAs() => SaveEditorAs(ActiveEditor);

    private bool SaveEditor(LabelEditor editor) =>
        editor.FilePath is { } path ? SaveEditorTo(editor, path) : SaveEditorAs(editor);

    private bool SaveEditorAs(LabelEditor editor)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = AppPaths.FileFilter, InitialDirectory = AppPaths.LabelsDir,
            FileName = editor.Title + AppPaths.Extension,
        };
        return dlg.ShowDialog(this) == DialogResult.OK && SaveEditorTo(editor, dlg.FileName);
    }

    private bool SaveEditorTo(LabelEditor editor, string path)
    {
        try
        {
            _library.Save(editor.Document, path);
            editor.FilePath = path;
            editor.MarkSaved();
            return true;
        }
        catch (Exception ex) { ShowError(Loc.T("Save"), ex); return false; }
    }

    // Exports the active label to a PNG, rendered at the printer's 203 dpi on a white background.
    private void ExportPng()
    {
        LabelEditor editor = ActiveEditor;
        using var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png", FileName = editor.Title + ".png",
            InitialDirectory = AppPaths.LabelsDir,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using Bitmap bmp = Core.Rendering.LabelRenderer.Render(
                editor.Document, Core.Rendering.RenderContext.ForPrint(), Color.White);
            bmp.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex) { ShowError(Loc.T("ExportPng"), ex); }
    }

    /// <summary>For a dirty editor, offers to save before it closes; returns false if the user cancels.</summary>
    private bool ConfirmSave(LabelEditor editor)
    {
        if (!editor.Dirty) return true;
        DialogResult r = MessageBox.Show(this, Loc.F("SaveChangesTo", editor.Title),
            Loc.T("AppTitle"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        return r switch
        {
            DialogResult.Yes => SaveEditor(editor),
            DialogResult.No => true,
            _ => false,
        };
    }

    private ContextMenuStrip RecentMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            IReadOnlyList<string> recent = _library.RecentFiles;
            if (recent.Count == 0)
            {
                menu.Items.Add(new ToolStripMenuItem("(no recent files)") { Enabled = false });
                return;
            }
            foreach (string path in recent)
            {
                string p = path;
                var item = new ToolStripMenuItem(Path.GetFileName(p)) { ToolTipText = p };
                item.Click += (_, _) => OpenRecent(p);
                menu.Items.Add(item);
            }
        };
        return menu;
    }

    private void ShowError(string action, Exception ex) =>
        MessageBox.Show(this, Loc.F("ActionFailed", action, ex.Message), action,
            MessageBoxButtons.OK, MessageBoxIcon.Error);

    // --- printing ---
    // Direct print: straight to the printer using the ribbon's current copies/offset, no dialog.
    private void PrintLabel()
    {
        if (EnsureConnected(out CT320BPrinter printer))
            DoPrint(printer, (uint)_copies.Value);
    }

    // Print preview: the dialog (live 1-bpp preview + copies/density/speed/gap/offset + out-of-bounds
    // guard). Clicking Print inside it prints with the chosen settings.
    private void ShowPrintPreview()
    {
        var doc = _canvas.Document;
        uint copies;
        using (var dlg = new PrintLabelForm(doc, (uint)_copies.Value))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            copies = dlg.Copies;
            _copies.Value = copies;                         // keep the ribbon spinner in sync
            _offsetX.Value = (decimal)doc.PrintOffsetXMm;   // dialog may have edited the offsets
            _offsetY.Value = (decimal)doc.PrintOffsetYMm;
        }
        if (EnsureConnected(out CT320BPrinter printer))
            DoPrint(printer, copies);
    }

    // --- batch / variable data (Phase 13) ---
    // Opens the batch dialog (counters + CSV merge + preview); on "Print run" prints the whole run.
    private void ShowBatchPrint()
    {
        LabelEditor editor = ActiveEditor;
        LabelDocument doc = editor.Document;
        bool print;
        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows;
        int labelCount;
        uint copies;
        using (var dlg = new BatchPrintForm(doc))
        {
            dlg.ShowDialog(this);
            if (dlg.CountersChanged) editor.MarkModified();
            print = dlg.ShouldPrint;
            rows = dlg.MergeRows;
            labelCount = dlg.LabelCount;
            copies = dlg.CopiesPerLabel;
        }
        if (!print || labelCount <= 0) return;
        if (EnsureConnected(out CT320BPrinter printer))
            DoBatchPrint(printer, doc, rows, labelCount, copies);
    }

    private async void DoBatchPrint(
        CT320BPrinter printer, LabelDocument template,
        IReadOnlyList<IReadOnlyDictionary<string, string>>? rows, int labelCount, uint copies)
    {
        _printBtn.Enabled = false;
        try
        {
            for (int i = 0; i < labelCount; i++)
            {
                int index = i;
                _printBtn.Text = Loc.F("PrintingN", index + 1, labelCount);
                LabelDocument label = BatchExpander.ExpandAt(template, rows, index);
                await Task.Run(() => LabelPrintJob.Print(printer, label, copies: copies));
            }
            AppLog.Success(Loc.F("BatchPrinted", labelCount));
        }
        catch (Exception ex)
        {
            AppLog.Error(Loc.F("BatchFailed", ex.Message));
        }
        finally
        {
            _printBtn.Enabled = true;
            _printBtn.Text = Loc.T("Print");
        }
    }

    // --- printer status & control panel (Phase 7) ---
    private void ShowControlPanel()
    {
        using var dlg = new ControlPanelForm(PrinterService);
        dlg.ShowDialog(this);
    }

    private bool EnsureConnected(out CT320BPrinter printer)
    {
        if (PrinterService.IsConnected && PrinterService.Printer is { } p)
        {
            printer = p;
            return true;
        }
        AppLog.Warn(Loc.T("ConnectFirst"));
        printer = null!;
        return false;
    }

    private async void DoPrint(CT320BPrinter printer, uint copies)
    {
        var doc = _canvas.Document;
        _printBtn.Enabled = false;
        _printBtn.Text = Loc.T("Printing");
        try
        {
            await Task.Run(() => LabelPrintJob.Print(printer, doc, copies: copies));
            AppLog.Success(copies > 1 ? Loc.F("LabelsPrinted", copies) : Loc.T("LabelPrinted"));
        }
        catch (Exception ex)
        {
            AppLog.Error(Loc.F("PrintFailed", ex.Message));
        }
        finally
        {
            _printBtn.Enabled = true;
            _printBtn.Text = Loc.T("Print");
        }
    }

    // --- shortcuts (don't hijack keys while editing a text/number field) ---
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool editing = FocusedLeaf() is TextBoxBase or NumericUpDown or ComboBox;
        switch (keyData)
        {
            case Keys.Control | Keys.N: NewLabel(); return true;
            case Keys.Control | Keys.O: OpenLabel(); return true;
            case Keys.Control | Keys.Shift | Keys.S: SaveLabelAs(); return true;
            case Keys.Control | Keys.S: SaveLabel(); return true;
            case Keys.Control | Keys.Shift | Keys.P: ShowPrintPreview(); return true;
            case Keys.Control | Keys.P: PrintLabel(); return true;
            case Keys.Control | Keys.B when !editing: ShowBatchPrint(); return true;
            case Keys.Control | Keys.W: _editorTabs.CloseEditor(ActiveEditor); return true;
            case Keys.Control | Keys.Z when !editing: _history.Undo(); return true;
            case Keys.Control | Keys.Y when !editing: _history.Redo(); return true;
            case Keys.Control | Keys.D when !editing: _canvas.DuplicateSelection(); return true;
            case Keys.Control | Keys.C when !editing: _canvas.CopySelection(); return true;
            case Keys.Control | Keys.X when !editing: _canvas.CutSelection(); return true;
            case Keys.Control | Keys.V when !editing: _canvas.Paste(); return true;
            case Keys.Control | Keys.A when !editing: _canvas.SelectAll(); return true;
            case Keys.Control | Keys.Shift | Keys.G when !editing: _canvas.UngroupSelection(); return true;
            case Keys.Control | Keys.G when !editing: _canvas.GroupSelection(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private Control? FocusedLeaf()
    {
        Control? c = this;
        while (c is ContainerControl { ActiveControl: { } active }) c = active;
        return c;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        foreach (LabelEditor editor in _editorTabs.Editors.ToList())
            if (!ConfirmSave(editor)) { e.Cancel = true; break; }
        if (!e.Cancel)
        {
            SaveSettings();
            AutoSaveService.ClearAll();   // clean shutdown → no recovery prompt next launch
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppLog.EntryAdded -= OnAppLogEntry;
            _autoSave?.Dispose();
            PrinterService.Dispose();
        }
        base.Dispose(disposing);
    }
}
