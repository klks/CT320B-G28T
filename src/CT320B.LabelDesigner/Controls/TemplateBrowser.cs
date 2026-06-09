using System.Drawing.Drawing2D;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Rendering;
using CT320B.LabelDesigner.Core.Serialization;
using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// The in-frame template browser shown under the ribbon's <b>Templates</b> tab: a thumbnailed list of a
/// blank label, the <b>User</b> and <b>Public</b> template folders (<c>.ct320b.json</c> / <c>.ddl</c>),
/// and the user's saved labels. Double-clicking an item raises <see cref="Opened"/> so the shell can add
/// a new document tab. (Replaces the old modal "New label" dialog.)
/// </summary>
public sealed class TemplateBrowser : UserControl
{
    private readonly TemplateLibrary _library;
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false, HideSelection = false,
        BorderStyle = BorderStyle.None, BackColor = Color.White,
    };
    private readonly ImageList _thumbs = new()
    {
        ImageSize = new Size(104, 86), ColorDepth = ColorDepth.Depth32Bit,
    };
    private readonly int _thumbW, _thumbH;

    // Background thumbnail loading: the list shows instantly with placeholders; thumbnails stream in
    // off the UI thread and are cached by path+mtime so repeat visits are instant.
    private CancellationTokenSource? _loadCts;
    private readonly Dictionary<string, Image> _thumbCache = [];
    private sealed record ThumbJob(ListViewItem Item, string CacheKey, Func<Image> Make);

    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = Loc.T("SearchTemplates") };
    private string _filter = "";

    /// <summary>Raised when a template/label is opened (double-click or Enter): the fresh document plus
    /// its backing path (non-null only for a saved label, so editing continues on that file).</summary>
    public event Action<LabelDocument, string?>? Opened;

    private sealed record Choice(Func<LabelDocument> Create, string? Path);

    public TemplateBrowser(TemplateLibrary library)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _thumbW = _thumbs.ImageSize.Width;
        _thumbH = _thumbs.ImageSize.Height;
        _list.LargeImageList = _thumbs;
        _list.ShowItemToolTips = true;   // hovering a template shows its file path
        _list.ItemActivate += (_, _) => OpenSelected();

        var header = new Label
        {
            Dock = DockStyle.Top, Height = 30, Text = "  " + Loc.T("TabTemplates"), Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.FromArgb(245, 245, 247),
        };

        // Search/filter row: filters the list by item name live (Phase 14c).
        var searchRow = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 3, 8, 4), BackColor = Color.FromArgb(245, 245, 247) };
        searchRow.Controls.Add(_search);
        _search.TextChanged += (_, _) => { _filter = _search.Text.Trim(); Reload(); };

        Controls.Add(_list);
        Controls.Add(searchRow);
        Controls.Add(header);
        // Populated lazily via Reload() the first time the Templates view is shown (parsing every
        // bundled preview is too slow to do at app startup).
    }

    /// <summary>Rebuilds the lists (call when the Templates view is shown, since files may have changed).
    /// The items appear immediately with placeholder thumbnails; the real thumbnails are computed off the
    /// UI thread and stream in (cached by path+mtime, so subsequent shows are instant).</summary>
    public void Reload()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        CancellationToken token = _loadCts.Token;

        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Groups.Clear();
        _thumbs.Images.Clear();
        _thumbs.Images.Add(Placeholder());   // index 0: shown until an item's real thumbnail arrives

        var jobs = new List<ThumbJob>();
        BuildItems(jobs);
        _list.EndUpdate();

        if (jobs.Count > 0) _ = LoadThumbsAsync(jobs, token);
    }

    private void BuildItems(List<ThumbJob> jobs)
    {
        var start = new ListViewGroup(Loc.T("Start"), HorizontalAlignment.Left);
        var userTemplates = new ListViewGroup(Loc.T("UserTemplates"), HorizontalAlignment.Left);
        var publics = new ListViewGroup(Loc.T("PublicTemplates"), HorizontalAlignment.Left);
        var saved = new ListViewGroup(Loc.T("SavedLabels"), HorizontalAlignment.Left);
        _list.Groups.AddRange([start, userTemplates, publics, saved]);

        // Blank presets are cheap — render their thumbnails synchronously. "Blank label" is the 30×40
        // default; the rest are common thermal-sticker sizes to start from.
        string blank = Loc.T("BlankLabel");
        if (Matches(blank))
            InstallNow(NewItem(blank, new Choice(BlankDocument, null), start), Thumbnail(BlankDocument()));
        foreach ((int w, int h) in CommonSizes)
        {
            string label = $"{w} × {h} mm";
            if (!Matches(label)) continue;
            int cw = w, ch = h;   // capture per iteration for the factory closure
            InstallNow(NewItem(label, new Choice(() => BlankDocument(cw, ch), null), start), Thumbnail(BlankDocument(cw, ch)));
        }

        foreach (LabelFileEntry f in _library.UserTemplates()) AddFileItem(f, userTemplates, jobs);
        foreach (LabelFileEntry f in _library.PublicTemplates()) AddFileItem(f, publics, jobs);
        foreach (LabelFileEntry f in _library.SavedLabels())
        {
            if (!Matches(f.Name)) continue;
            string path = f.Path;
            QueueItem(f.Name, new Choice(() => _library.Open(path), path), saved, path,
                () => TryLoad(path) is { } d ? Thumbnail(d) : Unavailable(), jobs);
        }
    }

    // Case-insensitive substring match against the current search filter (empty filter matches all).
    private bool Matches(string name) =>
        _filter.Length == 0 || name.Contains(_filter, StringComparison.OrdinalIgnoreCase);

    private void AddFileItem(LabelFileEntry f, ListViewGroup group, List<ThumbJob> jobs)
    {
        if (!Matches(f.Name)) return;
        string path = f.Path;
        bool ddl = path.EndsWith(".ddl", StringComparison.OrdinalIgnoreCase);
        Choice choice = ddl
            ? new Choice(() => DdlImporter.ImportFile(path).Document, null)
            : new Choice(() => LabelJson.Load(path), null);
        Func<Image> make = ddl
            ? () => DdlImporter.TryReadPreviewImage(path) is { } p ? FromImage(p) : Thumbnail(SafeImport(path))
            : () => TryLoad(path) is { } d ? Thumbnail(d) : Unavailable();
        QueueItem(f.Name, choice, group, path, make, jobs);
    }

    // Creates the list item now (placeholder thumbnail) and either reuses a cached thumbnail or queues
    // a background job to compute it.
    private void QueueItem(string title, Choice choice, ListViewGroup group, string path, Func<Image> make, List<ThumbJob> jobs)
    {
        ListViewItem item = NewItem(title, choice, group, path);
        string key = CacheKey(path);
        if (_thumbCache.TryGetValue(key, out Image? cached)) InstallNow(item, cached);
        else jobs.Add(new ThumbJob(item, key, make));
    }

    private ListViewItem NewItem(string title, Choice choice, ListViewGroup group, string? toolTip = null)
    {
        var item = new ListViewItem(title) { ImageIndex = 0, Tag = choice, Group = group, ToolTipText = toolTip ?? "" };
        _list.Items.Add(item);
        return item;
    }

    private void InstallNow(ListViewItem item, Image thumb)
    {
        _thumbs.Images.Add(thumb);
        item.ImageIndex = _thumbs.Images.Count - 1;
    }

    private async Task LoadThumbsAsync(List<ThumbJob> jobs, CancellationToken token)
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (ThumbJob job in jobs)
                {
                    if (token.IsCancellationRequested) return;
                    Image thumb;
                    try { thumb = job.Make(); } catch { thumb = Unavailable(); }
                    if (token.IsCancellationRequested || !IsHandleCreated) { thumb.Dispose(); return; }
                    try { BeginInvoke(() => Install(job, thumb, token)); }
                    catch (InvalidOperationException) { thumb.Dispose(); return; }   // handle gone
                }
            }, token);
        }
        catch (OperationCanceledException) { }
    }

    // UI thread: install a finished thumbnail (unless the list was rebuilt or the load cancelled).
    private void Install(ThumbJob job, Image thumb, CancellationToken token)
    {
        if (token.IsCancellationRequested || job.Item.ListView is null) { thumb.Dispose(); return; }
        _thumbCache[job.CacheKey] = thumb;
        _thumbs.Images.Add(thumb);
        job.Item.ImageIndex = _thumbs.Images.Count - 1;
    }

    private static string CacheKey(string path)
    {
        try { return path + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return path; }
    }

    private Bitmap Placeholder()
    {
        var b = new Bitmap(_thumbW, _thumbH);
        using Graphics g = Graphics.FromImage(b);
        g.Clear(Color.FromArgb(245, 245, 247));
        return b;
    }

    private Bitmap Unavailable()
    {
        var b = new Bitmap(_thumbW, _thumbH);
        using Graphics g = Graphics.FromImage(b);
        g.Clear(Color.FromArgb(238, 238, 240));
        using var f = new Font("Segoe UI", 8f);
        TextRenderer.DrawText(g, Loc.T("PreviewUnavailable"), f, new Rectangle(0, 0, _thumbW, _thumbH),
            Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        return b;
    }

    private void OpenSelected()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not Choice choice) return;
        try { Opened?.Invoke(choice.Create(), choice.Path); }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.F("OpenLabelErr", ex.Message), Loc.T("TabTemplates"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // Common thermal-sticker sizes (mm, W×H) offered as blank "Start" presets. 30×40 is "Blank label".
    private static readonly (int W, int H)[] CommonSizes =
    [
        (30, 20), (40, 30), (40, 40), (50, 30), (50, 50),
        (60, 40), (40, 60), (50, 70), (60, 80), (50, 80),
    ];

    private static LabelDocument BlankDocument() =>
        new() { Name = "Untitled", WidthMm = 30, HeightMm = 40, PrintOffsetXMm = -1f, PrintOffsetYMm = -1f };

    private static LabelDocument BlankDocument(float w, float h) =>
        new() { Name = $"{w:0.#} × {h:0.#}", WidthMm = w, HeightMm = h, PrintOffsetXMm = -1f, PrintOffsetYMm = -1f };

    private static LabelDocument SafeImport(string path)
    {
        try { return DdlImporter.ImportFile(path).Document; }
        catch (Exception ex) when (ex is IOException or FormatException or System.Xml.XmlException) { return BlankDocument(); }
    }

    private static LabelDocument? TryLoad(string path)
    {
        try { return LabelJson.Load(path); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { return null; }
    }

    private Image Thumbnail(LabelDocument doc)
    {
        int tw = _thumbW, th = _thumbH;
        var canvas = new Bitmap(tw, th);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.FromArgb(238, 238, 240));

        const float pad = 10f;
        double ppm = Math.Min((tw - pad) / Math.Max(1f, doc.WidthMm), (th - pad) / Math.Max(1f, doc.HeightMm));
        ppm = Math.Max(ppm, 0.1);
        try
        {
            using Bitmap label = LabelRenderer.Render(doc, RenderContext.ForScreen(ppm), Color.White);
            int x = (tw - label.Width) / 2, y = (th - label.Height) / 2;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(label, x, y, label.Width, label.Height);
            g.DrawRectangle(Pens.Silver, x, y, Math.Max(1, label.Width - 1), Math.Max(1, label.Height - 1));
        }
        catch
        {
            using var f = new Font("Segoe UI", 8f);
            TextRenderer.DrawText(g, Loc.T("PreviewUnavailable"), f, new Rectangle(0, 0, tw, th),
                Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        return canvas;
    }

    private Image FromImage(Image src)
    {
        int tw = _thumbW, th = _thumbH;
        var canvas = new Bitmap(tw, th);
        using (src)
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(238, 238, 240));
            const float pad = 10f;
            float scale = Math.Min((tw - pad) / src.Width, (th - pad) / src.Height);
            int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
            int x = (tw - w) / 2, y = (th - h) / 2;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, x, y, w, h);
            g.DrawRectangle(Pens.Silver, x, y, Math.Max(1, w - 1), Math.Max(1, h - 1));
        }
        return canvas;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            foreach (Image img in _thumbCache.Values) img.Dispose();
            _thumbCache.Clear();
            _thumbs.Dispose();
        }
        base.Dispose(disposing);
    }
}
