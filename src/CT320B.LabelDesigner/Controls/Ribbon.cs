using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// Renders the bundled Fluent UI System Icons (embedded SVGs) to tinted bitmaps for the ribbon and
/// insert bar. Icons are looked up by key (e.g. "print", "cut", "qr") = the embedded
/// <c>Icons\{key}.svg</c> file; results are cached by (key, size, colour). Monochrome recolour keeps
/// the SVG's alpha and replaces RGB, so any tint (including the blue print accent) stays crisp.
/// </summary>
public static class RibbonIcons
{
    public static readonly Color Ink = Color.FromArgb(64, 64, 68);
    public static readonly Color Accent = Color.FromArgb(43, 87, 154);   // Office blue

    private static readonly Dictionary<string, byte[]> _svgBytes = [];
    private static readonly Dictionary<(string key, int size, int argb), Image> _cache = [];

    /// <summary>Returns the icon for <paramref name="key"/> at <paramref name="size"/> px, tinted to
    /// <paramref name="color"/> (default <see cref="Ink"/>).</summary>
    public static Image Icon(string key, int size, Color? color = null)
    {
        Color c = color ?? Ink;
        var cacheKey = (key, size, c.ToArgb());
        if (_cache.TryGetValue(cacheKey, out Image? cached)) return cached;
        Image rendered = Render(key, size, c);
        _cache[cacheKey] = rendered;
        return rendered;
    }

    private static Bitmap Render(string key, int size, Color color)
    {
        var svg = Svg.SvgDocument.FromSvg<Svg.SvgDocument>(System.Text.Encoding.UTF8.GetString(LoadSvg(key)));
        Bitmap bmp = svg.Draw(size, size);
        Tint(bmp, color);
        return bmp;
    }

    private static byte[] LoadSvg(string key)
    {
        if (_svgBytes.TryGetValue(key, out byte[]? data)) return data;
        Assembly asm = typeof(RibbonIcons).Assembly;
        string resource = asm.GetManifestResourceNames()
            .First(n => n.EndsWith($".Icons.{key}.svg", StringComparison.OrdinalIgnoreCase));
        using Stream s = asm.GetManifestResourceStream(resource)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        data = ms.ToArray();
        _svgBytes[key] = data;
        return data;
    }

    // Recolour a monochrome-on-transparent icon: keep each pixel's alpha, set its RGB to the tint.
    private static void Tint(Bitmap bmp, Color color)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[bmp.Width * 4];
            for (int y = 0; y < bmp.Height; y++)
            {
                IntPtr line = data.Scan0 + y * data.Stride;
                Marshal.Copy(line, row, 0, row.Length);
                for (int x = 0; x < bmp.Width; x++)
                {
                    int i = x * 4;
                    if (row[i + 3] == 0) continue;   // leave fully-transparent pixels
                    row[i] = color.B; row[i + 1] = color.G; row[i + 2] = color.R;   // BGRA in memory
                }
                Marshal.Copy(row, 0, line, row.Length);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}

/// <summary>
/// A Microsoft-Office-style ribbon: a tab strip whose pages hold groups of large/small icon
/// buttons with a group title underneath and separators between groups. Built on a themed
/// <see cref="TabControl"/> + flow-laid <see cref="RibbonGroup"/>s (WinForms has no native ribbon).
/// When a tab is too narrow for all its groups, lower-priority groups progressively collapse into a
/// single labelled dropdown button (see <see cref="RibbonGroup"/>), restoring as width returns.
/// </summary>
public sealed class RibbonControl : UserControl
{
    private readonly TabControl _tabs = new();
    private readonly List<RibbonTab> _ribbonTabs = [];

    /// <summary>Raised when the selected ribbon tab changes (see <see cref="SelectedTabName"/>).</summary>
    public event EventHandler? SelectedTabChanged;

    /// <summary>The stable key of the currently selected ribbon tab, or null. (Independent of the
    /// localised display text — see <see cref="AddTab"/>.)</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedTabName => _tabs.SelectedTab?.Name;

    public RibbonControl()
    {
        Dock = DockStyle.Top;
        Height = 150;   // room for a 3-button small column + its group label without clipping
        BackColor = Color.White;
        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(14, 4);   // wider, taller tab headers (Office-like)
        _tabs.SizeMode = TabSizeMode.Normal;
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            ReflowActiveTab();
            SelectedTabChanged?.Invoke(this, EventArgs.Empty);
        };
        Controls.Add(_tabs);
    }

    /// <summary>Selects the ribbon tab with the given stable key, if present.</summary>
    public void SelectTab(string name)
    {
        foreach (TabPage page in _tabs.TabPages)
            if (page.Name == name) { _tabs.SelectedTab = page; return; }
    }

    /// <summary>Adds a ribbon tab and returns it for populating with groups. <paramref name="name"/> is a
    /// stable key (used by <see cref="SelectTab"/>/<see cref="SelectedTabName"/>); <paramref name="text"/>
    /// is the localised header shown to the user (defaults to the key).</summary>
    public RibbonTab AddTab(string name, string? text = null)
    {
        var page = new TabPage(text ?? name) { Name = name, BackColor = Color.White, Padding = new Padding(4, 4, 4, 2) };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = false, BackColor = Color.White,
        };
        page.Controls.Add(flow);
        _tabs.TabPages.Add(page);
        var tab = new RibbonTab(flow);
        // Reflow when the host panel reaches its final width — the page resizes *after* the ribbon's
        // own SizeChanged, so reading the host width there would be stale.
        flow.SizeChanged += (_, _) => tab.Reflow();
        _ribbonTabs.Add(tab);
        return tab;
    }

    // Re-measure the selected tab's groups and collapse/expand them to fit the current width.
    private void ReflowActiveTab()
    {
        int idx = _tabs.SelectedIndex;
        if (idx >= 0 && idx < _ribbonTabs.Count) _ribbonTabs[idx].Reflow();
    }
}

/// <summary>One ribbon tab page; hosts <see cref="RibbonGroup"/>s left-to-right and drives their
/// responsive collapse when the row is wider than the available space.</summary>
public sealed class RibbonTab(FlowLayoutPanel host)
{
    private readonly List<RibbonGroup> _groups = [];

    /// <summary>Adds a group. <paramref name="iconKey"/> is the icon shown when the group collapses
    /// to a dropdown button; lower <paramref name="collapsePriority"/> groups collapse first.</summary>
    public RibbonGroup AddGroup(string title, string iconKey = "box", int collapsePriority = 0)
    {
        var group = new RibbonGroup(title, iconKey, collapsePriority);
        host.Controls.Add(group);
        _groups.Add(group);
        return group;
    }

    /// <summary>Collapses the lowest-priority groups (into dropdown buttons) until the row fits the
    /// host width, expanding the rest. Idempotent — only groups whose state changes are touched.</summary>
    internal void Reflow()
    {
        if (_groups.Count == 0) return;
        int available = host.ClientSize.Width;
        if (available <= 0) return;

        int total = _groups.Sum(g => g.ExpandedWidth);
        var collapse = new HashSet<RibbonGroup>();
        foreach (RibbonGroup g in _groups.OrderBy(g => g.CollapsePriority))
        {
            if (total <= available) break;
            collapse.Add(g);
            total -= g.ExpandedWidth - g.CollapsedWidth;
        }

        host.SuspendLayout();
        foreach (RibbonGroup g in _groups) g.SetCollapsed(collapse.Contains(g));
        host.ResumeLayout(true);
    }
}

/// <summary>
/// A labelled ribbon group: a content row of buttons above a centered title, with a faint vertical
/// separator on its right edge. When the ribbon is too narrow the group <see cref="SetCollapsed"/>s
/// to a single large dropdown button (group icon + title) whose popup hosts the same controls.
/// </summary>
public sealed class RibbonGroup : Panel
{
    private readonly FlowLayoutPanel _content;
    private readonly TableLayoutPanel _table;
    private readonly Button _collapsedButton;
    private int _expandedWidth;          // cached preferred width when expanded (content is fixed)
    private Form? _popup;                // open popup hosting _content while collapsed, else null
    private DateTime _popupClosedAt;     // guards click-to-reopen right after a click-away close

    /// <summary>Lower values collapse first when the ribbon runs out of room.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CollapsePriority { get; }

    /// <summary>Whether the group is currently shown as a single dropdown button.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsCollapsed { get; private set; }

    public RibbonGroup(string title, string iconKey = "box", int collapsePriority = 0)
    {
        CollapsePriority = collapsePriority;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = new Padding(3, 2, 5, 0);
        BackColor = Color.White;

        _table = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
            ColumnCount = 1, RowCount = 2, BackColor = Color.White,
        };
        _table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));

        _content = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Top | AnchorStyles.Left,
            BackColor = Color.White,
        };
        var label = new Label
        {
            // AutoSize so the title contributes only its text width to the group's auto-size — otherwise a
            // non-autosize Label's default 100px width forces every group ≥100px wide, leaving a gap to the
            // right of a narrow single-button group (e.g. Printer ▸ Control).
            Text = title, AutoSize = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gray, Font = new Font("Segoe UI", 7.5f),
        };
        _table.Controls.Add(_content, 0, 0);
        _table.Controls.Add(label, 0, 1);
        Controls.Add(_table);

        // The collapsed representation: a large dropdown button, hidden until the group collapses.
        _collapsedButton = MakeLarge(title + " ▾", RibbonIcons.Icon(iconKey, 32, RibbonIcons.Ink));
        _collapsedButton.Visible = false;
        _collapsedButton.Click += (_, _) => TogglePopup();
        Controls.Add(_collapsedButton);
    }

    /// <summary>Preferred width of the group when fully expanded (cached; content is built once).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ExpandedWidth
    {
        get
        {
            if (_expandedWidth == 0)
                _expandedWidth = _table.GetPreferredSize(Size.Empty).Width + Padding.Horizontal;
            return _expandedWidth;
        }
    }

    /// <summary>Width of the group when collapsed to its single dropdown button.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CollapsedWidth =>
        _collapsedButton.GetPreferredSize(Size.Empty).Width + Padding.Horizontal;

    /// <summary>Switches between the full content and the single dropdown button. No-op if unchanged.</summary>
    internal void SetCollapsed(bool collapsed)
    {
        if (collapsed == IsCollapsed) return;
        ClosePopup();
        IsCollapsed = collapsed;
        _table.Visible = !collapsed;
        _collapsedButton.Visible = collapsed;
    }

    private void TogglePopup()
    {
        // Clicking the button while its popup is open first fires the popup's Deactivate (which closes
        // it), then this Click — without this guard it would immediately reopen.
        if (_popup != null || (DateTime.UtcNow - _popupClosedAt).TotalMilliseconds < 250) { ClosePopup(); return; }

        var popup = new Form
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
            ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White, Padding = new Padding(7),
        };
        popup.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(190, 190, 196));
            e.Graphics.DrawRectangle(pen, 0, 0, popup.ClientSize.Width - 1, popup.ClientSize.Height - 1);
        };
        _table.Controls.Remove(_content);
        popup.Controls.Add(_content);
        popup.Location = _collapsedButton.PointToScreen(new Point(0, _collapsedButton.Height));
        popup.Deactivate += (_, _) => ClosePopup();
        _popup = popup;
        popup.Show(FindForm());
    }

    private void ClosePopup()
    {
        if (_popup == null) return;
        Form p = _popup;
        _popup = null;
        _popupClosedAt = DateTime.UtcNow;
        p.Controls.Remove(_content);
        _table.Controls.Add(_content, 0, 0);   // return content to its cell (table may stay hidden)
        p.Close();
        p.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(225, 225, 228));
        e.Graphics.DrawLine(pen, Width - 4, 6, Width - 4, Height - 8);
    }

    /// <summary>A large icon-over-text button (icon from an icon key).</summary>
    public Button AddLarge(string text, string iconKey, Action onClick, Color? iconColor = null) =>
        AddLarge(text, RibbonIcons.Icon(iconKey, 32, iconColor ?? RibbonIcons.Ink), onClick);

    /// <summary>A large icon-over-text button (icon from a supplied image).</summary>
    public Button AddLarge(string text, Image image, Action onClick)
    {
        Button b = MakeLarge(text, image);
        b.Click += (_, _) => onClick();
        _content.Controls.Add(b);
        return b;
    }

    /// <summary>A large button that opens a dropdown menu when clicked.</summary>
    public Button AddLargeMenu(string text, string iconKey, ContextMenuStrip menu)
    {
        Button b = MakeLarge(text + " ▾", RibbonIcons.Icon(iconKey, 32, RibbonIcons.Ink));
        b.Click += (_, _) => menu.Show(b, new Point(0, b.Height));
        _content.Controls.Add(b);
        return b;
    }

    /// <summary>A vertical stack of labelled checkboxes (Office "Show" group style — compact toggles).</summary>
    public CheckBox[] AddCheckColumn(params (string text, bool initial, Action<bool> onToggle)[] items)
    {
        var col = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = new Padding(2, 4, 2, 0), BackColor = Color.White,
        };
        var made = new CheckBox[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            (string text, bool initial, Action<bool> onToggle) = items[i];
            var c = new CheckBox
            {
                Text = text, AutoSize = true, Checked = initial,
                Margin = new Padding(0, 1, 0, 1), Font = new Font("Segoe UI", 8.5f),
            };
            c.CheckedChanged += (_, _) => onToggle(c.Checked);
            col.Controls.Add(c);
            made[i] = c;
        }
        _content.Controls.Add(col);
        return made;
    }

    /// <summary>A vertical stack of small icon+text buttons (up to ~3 per Office convention).</summary>
    public Button[] AddSmallColumn(params (string text, string iconKey, Action onClick)[] items)
    {
        var col = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, Margin = new Padding(1, 1, 1, 0), BackColor = Color.White,
        };
        var made = new Button[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            (string text, string iconKey, Action onClick) = items[i];
            Button b = MakeSmall(text, iconKey);
            b.Click += (_, _) => onClick();
            col.Controls.Add(b);
            made[i] = b;
        }
        _content.Controls.Add(col);
        return made;
    }

    /// <summary>Hosts an arbitrary control (e.g. a copies spinner) in the group.</summary>
    public void AddControl(Control control)
    {
        control.Margin = new Padding(3, 2, 3, 2);
        _content.Controls.Add(control);
    }

    private static Button MakeLarge(string text, Image image)
    {
        var b = new Button
        {
            Text = text, Image = image,
            TextImageRelation = TextImageRelation.ImageAboveText, ImageAlign = ContentAlignment.TopCenter,
            TextAlign = ContentAlignment.BottomCenter, FlatStyle = FlatStyle.Flat,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(48, 66),
            Padding = new Padding(4, 4, 4, 2), Margin = new Padding(0, 0, 1, 0), Font = new Font("Segoe UI", 8f),
            UseVisualStyleBackColor = true,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 241, 251);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(204, 228, 247);
        return b;
    }

    private static Button MakeSmall(string text, string iconKey)
    {
        var b = new Button
        {
            Text = "  " + text, Image = RibbonIcons.Icon(iconKey, 16, RibbonIcons.Ink),
            TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(82, 24),
            Padding = new Padding(2, 2, 8, 2), Margin = new Padding(0, 0, 0, 1), Font = new Font("Segoe UI", 8.25f),
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 241, 251);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(204, 228, 247);
        return b;
    }
}
