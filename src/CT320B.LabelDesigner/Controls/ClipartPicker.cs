using System.Drawing.Drawing2D;
using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// Picks a bundled clip-art / emoji image (from <see cref="AppPaths.ClipartDir"/>'s category folders):
/// a category dropdown plus a thumbnail grid. The chosen file is returned as <see cref="SelectedPath"/>;
/// the caller embeds its bytes into an <see cref="Core.Model.Elements.ImageElement"/>.
/// </summary>
public sealed class ClipartPicker : Form
{
    private readonly ComboBox _category = new()
    {
        Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(8),
    };
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.LargeIcon, MultiSelect = false, BorderStyle = BorderStyle.None,
        BackColor = Color.White,
    };
    private readonly ImageList _thumbs = new() { ImageSize = new Size(52, 52), ColorDepth = ColorDepth.Depth32Bit };

    /// <summary>The chosen image file path (valid after the dialog returns OK).</summary>
    public string? SelectedPath { get; private set; }

    public ClipartPicker()
    {
        Text = Loc.T("InsertClipart");
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 480);
        MinimumSize = new Size(420, 360);
        ShowInTaskbar = false;
        MinimizeBox = false;

        _list.LargeImageList = _thumbs;
        _list.ItemActivate += (_, _) => Choose();
        _list.SelectedIndexChanged += (_, _) => { };

        var top = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 6, 8, 4) };
        _category.Width = 200;
        top.Controls.Add(_category);

        var buttons = new FlowLayoutPanel
        { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        var cancel = new Button { Text = Loc.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var insert = new Button { Text = Loc.T("Insert"), AutoSize = true };
        insert.Click += (_, _) => Choose();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(insert);

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(buttons);
        AcceptButton = insert;
        CancelButton = cancel;

        LoadCategories();
        _category.SelectedIndexChanged += (_, _) => LoadThumbnails();
        LoadThumbnails();
    }

    private void LoadCategories()
    {
        _category.Items.Add(Loc.T("AllCategories"));
        if (Directory.Exists(AppPaths.ClipartDir))
            foreach (string dir in Directory.EnumerateDirectories(AppPaths.ClipartDir).OrderBy(d => d))
                _category.Items.Add(Path.GetFileName(dir));
        _category.SelectedIndex = _category.Items.Count > 1 ? 1 : 0;   // first real category (fast initial load)
    }

    private IEnumerable<string> CurrentFiles()
    {
        if (!Directory.Exists(AppPaths.ClipartDir)) return [];
        // Index 0 is the localised "All" entry; others are real category folder names.
        bool all = _category.SelectedIndex <= 0;
        string cat = _category.SelectedItem as string ?? "";
        string root = all ? AppPaths.ClipartDir : Path.Combine(AppPaths.ClipartDir, cat);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).OrderBy(p => p);
    }

    private void LoadThumbnails()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        _thumbs.Images.Clear();
        foreach (string file in CurrentFiles())
        {
            _thumbs.Images.Add(Thumbnail(file));
            _list.Items.Add(new ListViewItem(Path.GetFileNameWithoutExtension(file))
            {
                ImageIndex = _thumbs.Images.Count - 1, Tag = file, ToolTipText = Path.GetFileNameWithoutExtension(file),
            });
        }
        _list.EndUpdate();
    }

    private void Choose()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not string path) return;
        SelectedPath = path;
        DialogResult = DialogResult.OK;
        Close();
    }

    private Image Thumbnail(string file)
    {
        int s = _thumbs.ImageSize.Width;
        var canvas = new Bitmap(s, s);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.White);
        try
        {
            using var src = Image.FromFile(file);
            float scale = Math.Min((s - 6f) / src.Width, (s - 6f) / src.Height);
            int w = Math.Max(1, (int)(src.Width * scale)), h = Math.Max(1, (int)(src.Height * scale));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, (s - w) / 2, (s - h) / 2, w, h);
        }
        catch { /* unreadable → blank tile */ }
        return canvas;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _thumbs.Dispose();
        base.Dispose(disposing);
    }
}
