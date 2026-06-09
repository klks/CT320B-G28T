using System.ComponentModel;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Printing;
using CT320B.LabelDesigner.Core.VariableData;
using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// The variable-data / batch dialog (Phase 13c). Edits the document's serial <see cref="SerialCounter"/>s,
/// optionally loads a CSV/TSV mail-merge source, previews the generated labels one at a time, and — on
/// <b>Print run</b> — hands the merge inputs back to the shell to print the whole run. Fields bind data
/// with <c>{token}</c> placeholders (counter names or CSV columns); editing the actual fields happens in
/// the main editor, this dialog just shows which tokens are referenced and which are available.
/// </summary>
public sealed class BatchPrintForm : Form
{
    private readonly LabelDocument _doc;
    private readonly BindingList<SerialCounter> _counters;
    private CsvData? _csv;

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = true, AllowUserToAddRows = true,
        AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersWidth = 24, BackgroundColor = SystemColors.Window,
    };
    private readonly PictureBox _preview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(96, 96, 100) };
    private readonly Label _csvLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = Loc.T("NoDataFile") };
    private readonly Label _tokenLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true };
    private readonly Label _navLabel = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "—" };
    private readonly NumericUpDown _labelCount = new() { Minimum = 1, Maximum = 100000, Value = 1, Width = 80 };
    private readonly NumericUpDown _copies = new() { Minimum = 1, Maximum = 999, Value = 1, Width = 70 };
    private readonly Button _prev = new() { Text = "◀", Width = 40 };
    private readonly Button _next = new() { Text = "▶", Width = 40 };

    private int _index;

    /// <summary>True after the user clicked <b>Print run</b> (the dialog closes with <see cref="DialogResult.OK"/>).</summary>
    public bool ShouldPrint { get; private set; }

    /// <summary>The loaded merge rows, or null for a counter-only batch.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>>? MergeRows => _csv?.Rows;

    /// <summary>How many distinct labels the run produces (one per merge row, else the counter-only count).</summary>
    public int LabelCount => BatchExpander.RowCount(MergeRows, (int)_labelCount.Value);

    /// <summary>Copies to print of each distinct label.</summary>
    public uint CopiesPerLabel => (uint)_copies.Value;

    /// <summary>True when the user changed the document's counters (so the shell can mark the doc modified).</summary>
    public bool CountersChanged { get; private set; }

    public BatchPrintForm(LabelDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        _doc = doc;
        _counters = new BindingList<SerialCounter>(doc.Counters.Select(Copy).ToList());

        Text = Loc.T("BatchTitle");
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 540);
        Size = new Size(980, 620);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(BuildPreviewPane());
        split.Panel2.Controls.Add(BuildControlsPane());
        // The split's real width isn't known until layout; set the divider/min once it is.
        Load += (_, _) =>
        {
            split.Panel2MinSize = 300;
            split.Panel1MinSize = 280;
            split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Width - 360);
        };

        Controls.Add(split);
        Controls.Add(BuildButtonBar());

        _grid.DataSource = _counters;
        _counters.ListChanged += (_, _) => { CountersChanged = true; RefreshTokens(); UpdatePreview(); };
        _grid.CellEndEdit += (_, _) => { RefreshTokens(); UpdatePreview(); };
        _prev.Click += (_, _) => { _index--; UpdatePreview(); };
        _next.Click += (_, _) => { _index++; UpdatePreview(); };
        _labelCount.ValueChanged += (_, _) => UpdatePreview();
        _copies.ValueChanged += (_, _) => UpdatePreview();

        RefreshTokens();
        UpdatePreview();
    }

    private static SerialCounter Copy(SerialCounter c) => new()
    {
        Name = c.Name, Start = c.Start, Step = c.Step, Padding = c.Padding, Prefix = c.Prefix, Suffix = c.Suffix,
    };

    private Control BuildPreviewPane()
    {
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var nav = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 36, ColumnCount = 3, RowCount = 1 };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        nav.Controls.Add(_prev, 0, 0);
        nav.Controls.Add(_navLabel, 1, 0);
        nav.Controls.Add(_next, 2, 0);

        host.Controls.Add(_preview);
        host.Controls.Add(nav);
        return host;
    }

    private Control BuildControlsPane()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));   // data file
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // counters grid
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));   // tokens
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // run size

        // --- data file group ---
        var dataGroup = new GroupBox { Text = Loc.T("MailMergeData"), Dock = DockStyle.Fill };
        var dataInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(6) };
        dataInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dataInner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        var load = new Button { Text = Loc.T("LoadEllipsis"), Dock = DockStyle.Fill };
        var clear = new Button { Text = Loc.T("Clear"), Dock = DockStyle.Fill };
        load.Click += (_, _) => LoadCsv();
        clear.Click += (_, _) => { _csv = null; RefreshCsvLabel(); RefreshTokens(); _index = 0; UpdatePreview(); };
        dataInner.Controls.Add(_csvLabel, 0, 0);
        dataInner.Controls.Add(load, 1, 0);
        dataInner.SetColumnSpan(_csvLabel, 1);
        dataInner.Controls.Add(clear, 1, 1);
        dataGroup.Controls.Add(dataInner);

        // --- counters grid ---
        var countersGroup = new GroupBox { Text = Loc.T("SerialCounters"), Dock = DockStyle.Fill };
        countersGroup.Controls.Add(_grid);

        // --- tokens ---
        var tokenGroup = new GroupBox { Text = Loc.T("FieldBindings"), Dock = DockStyle.Fill };
        tokenGroup.Controls.Add(_tokenLabel);

        // --- run size ---
        var runPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        runPanel.Controls.Add(new Label { Text = Loc.T("LabelsColon"), AutoSize = true, Margin = new Padding(0, 9, 4, 0) });
        runPanel.Controls.Add(_labelCount);
        runPanel.Controls.Add(new Label { Text = Loc.T("CopiesEach"), AutoSize = true, Margin = new Padding(16, 9, 4, 0) });
        runPanel.Controls.Add(_copies);

        root.Controls.Add(dataGroup, 0, 0);
        root.Controls.Add(countersGroup, 0, 1);
        root.Controls.Add(tokenGroup, 0, 2);
        root.Controls.Add(runPanel, 0, 3);
        return root;
    }

    private Control BuildButtonBar()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        var print = new Button { Text = Loc.T("PrintRun"), Width = 110, Height = 28, DialogResult = DialogResult.OK };
        var close = new Button { Text = Loc.T("Close"), Width = 90, Height = 28, DialogResult = DialogResult.Cancel };
        print.Click += (_, _) => { SyncCounters(); ShouldPrint = true; };
        close.Click += (_, _) => SyncCounters();
        bar.Controls.Add(print);
        bar.Controls.Add(close);
        AcceptButton = print;
        CancelButton = close;
        return bar;
    }

    // Writes the edited counters back to the document (dropping blank-name rows from the grid's new-row).
    private void SyncCounters()
    {
        _doc.Counters.Clear();
        foreach (SerialCounter c in _counters)
            if (!string.IsNullOrWhiteSpace(c.Name)) _doc.Counters.Add(c);
    }

    private void LoadCsv()
    {
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("LoadMergeData"),
            Filter = "Data files (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _csv = CsvData.Load(dlg.FileName);
            _index = 0;
            RefreshCsvLabel();
            RefreshTokens();
            UpdatePreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.F("DataFileReadErr", ex.Message), Loc.T("Batch"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshCsvLabel() => _csvLabel.Text = _csv is null
        ? Loc.T("NoDataFile")
        : Loc.F("CsvSummary", _csv.Rows.Count, string.Join(", ", _csv.Columns));

    // Shows the {tokens} the design references and whether each has a source (counter or CSV column).
    private void RefreshTokens()
    {
        SyncCountersPreviewOnly();
        IReadOnlyList<string> referenced = BatchExpander.ReferencedTokens(_doc);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SerialCounter c in _counters) if (!string.IsNullOrWhiteSpace(c.Name)) sources.Add(c.Name.Trim());
        if (_csv is not null) foreach (string col in _csv.Columns) sources.Add(col);

        if (referenced.Count == 0)
        {
            _tokenLabel.ForeColor = SystemColors.GrayText;
            _tokenLabel.Text = Loc.T("TokenHelpNone");
            return;
        }
        var unbound = referenced.Where(t => !sources.Contains(t)).ToList();
        string text = Loc.F("TokenUsed", string.Join(", ", referenced.Select(t => "{" + t + "}")));
        if (unbound.Count > 0) text += "\n" + Loc.F("TokenNoSource", string.Join(", ", unbound.Select(t => "{" + t + "}")));
        _tokenLabel.ForeColor = unbound.Count > 0 ? Color.FromArgb(176, 0, 32) : SystemColors.ControlText;
        _tokenLabel.Text = text;
    }

    // Mirror the grid edits onto the doc's counters so expansion/preview reflect in-progress edits,
    // without committing the new-row placeholder.
    private void SyncCountersPreviewOnly()
    {
        _doc.Counters.Clear();
        foreach (SerialCounter c in _counters)
            if (!string.IsNullOrWhiteSpace(c.Name)) _doc.Counters.Add(c);
    }

    private void UpdatePreview()
    {
        SyncCountersPreviewOnly();
        int count = LabelCount;
        _index = Math.Clamp(_index, 0, Math.Max(0, count - 1));
        _prev.Enabled = _index > 0;
        _next.Enabled = _index < count - 1;
        _labelCount.Enabled = _csv is null;   // CSV row count drives the size when present
        _navLabel.Text = count > 0 ? Loc.F("LabelOfCount", _index + 1, count) : Loc.T("NoLabels");

        Image? old = _preview.Image;
        try
        {
            LabelDocument expanded = BatchExpander.ExpandAt(_doc, MergeRows, _index);
            _preview.Image = LabelPrintJob.RenderForPrint(expanded);
        }
        catch
        {
            _preview.Image = null;
        }
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Image?.Dispose();
        base.Dispose(disposing);
    }
}
