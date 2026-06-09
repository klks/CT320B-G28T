using System.Drawing.Drawing2D;
using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Core.Printing;
using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// The print dialog: a live preview of the <b>actual 1-bpp bitmap</b> the printer will mark (from
/// <see cref="LabelPrintJob.RenderMonochromePreview"/>) on the left, and the print parameters
/// (copies, density, speed, gap, X/Y offset) on the right. It writes density/speed/gap/offset back to
/// the document as they change and re-renders the preview, warns if content would be clipped, and
/// returns <see cref="DialogResult.OK"/> with the chosen <see cref="Copies"/> when the user confirms.
/// The caller performs the (async) print.
/// </summary>
public sealed class PrintLabelForm : Form
{
    private readonly LabelDocument _doc;
    private readonly PreviewPanel _preview = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown _copies = Spin(1, 99, 1, 0);
    private readonly NumericUpDown _density = Spin(0, 15, 1, 0);
    private readonly NumericUpDown _speed = Spin(1, 14, 1, 0);
    private readonly NumericUpDown _gap = Spin(0, 50, 0.5m, 1);
    private readonly NumericUpDown _offsetX = Spin(-20, 20, 0.5m, 1);
    private readonly NumericUpDown _offsetY = Spin(-20, 20, 0.5m, 1);
    private readonly Label _warning = new()
    {
        Dock = DockStyle.Top, AutoSize = false, Height = 0, Visible = false,
        ForeColor = Color.FromArgb(140, 70, 0), BackColor = Color.FromArgb(255, 244, 206),
        Padding = new Padding(8, 6, 8, 6), TextAlign = ContentAlignment.MiddleLeft,
    };

    /// <summary>The number of copies the user chose (valid after the dialog returns OK).</summary>
    public uint Copies => (uint)_copies.Value;

    public PrintLabelForm(LabelDocument document, uint initialCopies)
    {
        _doc = document ?? throw new ArgumentNullException(nameof(document));

        Text = Loc.T("Print");
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640, 460);
        ClientSize = new Size(760, 520);
        ShowInTaskbar = false;
        MinimizeBox = false;

        _copies.Value = Math.Clamp(initialCopies, 1, 99);
        _density.Value = Math.Clamp(_doc.Density, 0, 15);
        _speed.Value = Math.Clamp(_doc.Speed, 1, 14);
        _gap.Value = (decimal)Math.Clamp(_doc.GapMm, 0f, 50f);
        _offsetX.Value = (decimal)Math.Clamp(_doc.PrintOffsetXMm, -20f, 20f);
        _offsetY.Value = (decimal)Math.Clamp(_doc.PrintOffsetYMm, -20f, 20f);

        _density.ValueChanged += (_, _) => _doc.Density = (int)_density.Value;
        _speed.ValueChanged += (_, _) => _doc.Speed = (int)_speed.Value;
        _gap.ValueChanged += (_, _) => _doc.GapMm = (float)_gap.Value;
        // Offset changes the rendered raster → re-render the preview.
        _offsetX.ValueChanged += (_, _) => { _doc.PrintOffsetXMm = (float)_offsetX.Value; RefreshPreview(); };
        _offsetY.ValueChanged += (_, _) => { _doc.PrintOffsetYMm = (float)_offsetY.Value; RefreshPreview(); };

        Controls.Add(BuildPreviewPane());
        Controls.Add(BuildSettingsPane());

        RefreshPreview();
    }

    private Control BuildPreviewPane()
    {
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var caption = new Label
        {
            Dock = DockStyle.Bottom, Height = 22, ForeColor = Color.DimGray,
            TextAlign = ContentAlignment.MiddleCenter,
            Text = Loc.F("ActualOutput", Units.MmToDots(_doc.WidthMm), Units.MmToDots(_doc.HeightMm)),
        };
        host.Controls.Add(_preview);
        host.Controls.Add(caption);
        host.Controls.Add(_warning);
        return host;
    }

    private Control BuildSettingsPane()
    {
        var pane = new Panel { Dock = DockStyle.Right, Width = 232, Padding = new Padding(12) };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 6,
            Padding = new Padding(0, 4, 0, 0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddRow(grid, Loc.T("Copies"), _copies);
        AddRow(grid, Loc.T("Density"), _density);
        AddRow(grid, Loc.T("Speed"), _speed);
        AddRow(grid, Loc.T("GapMm"), _gap);
        AddRow(grid, Loc.T("OffsetXMm"), _offsetX);
        AddRow(grid, Loc.T("OffsetYMm"), _offsetY);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true, Padding = new Padding(0, 8, 0, 0),
        };
        var cancel = new Button { Text = Loc.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var print = new Button { Text = Loc.T("Print"), AutoSize = true, Image = RibbonIcons.Icon("print", 16, RibbonIcons.Accent), TextImageRelation = TextImageRelation.ImageBeforeText };
        print.Click += OnPrintClick;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(print);

        AcceptButton = print;
        CancelButton = cancel;

        pane.Controls.Add(grid);
        pane.Controls.Add(buttons);
        return pane;
    }

    private void OnPrintClick(object? sender, EventArgs e)
    {
        IReadOnlyList<LabelElement> outside = LabelBounds.FindOutOfBounds(_doc);
        if (outside.Count > 0)
        {
            string names = string.Join(", ", outside.Select(el => string.IsNullOrEmpty(el.Name) ? el.GetType().Name : el.Name).Take(5));
            DialogResult go = MessageBox.Show(this,
                Loc.F("OutOfBoundsPrompt", outside.Count, names),
                Loc.T("OutOfBounds"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (go != DialogResult.Yes) return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private void RefreshPreview()
    {
        Bitmap bmp = LabelPrintJob.RenderMonochromePreview(_doc);
        _preview.SetImage(bmp);

        IReadOnlyList<LabelElement> outside = LabelBounds.FindOutOfBounds(_doc);
        if (outside.Count > 0)
        {
            _warning.Text = Loc.F("OutOfBoundsWarn", outside.Count);
            _warning.Height = 30;
            _warning.Visible = true;
        }
        else
        {
            _warning.Visible = false;
            _warning.Height = 0;
        }
    }

    private static NumericUpDown Spin(decimal min, decimal max, decimal step, int decimals) => new()
    {
        Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals,
        Width = 64, TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Right,
    };

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 6) });
        grid.Controls.Add(control);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _preview.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>A double-buffered panel that paints a bitmap fit-to-area with no smoothing, so the
    /// real printer dots are visible (a "desk" background + the label edge border).</summary>
    private sealed class PreviewPanel : Panel
    {
        private Bitmap? _image;

        public PreviewPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(96, 96, 100);
        }

        public void SetImage(Bitmap image)
        {
            _image?.Dispose();
            _image = image;
            Invalidate();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_image is not { } img) return;

            Rectangle area = ClientRectangle;
            area.Inflate(-12, -12);
            if (area.Width <= 0 || area.Height <= 0) return;

            float scale = Math.Min((float)area.Width / img.Width, (float)area.Height / img.Height);
            int w = Math.Max(1, (int)(img.Width * scale));
            int h = Math.Max(1, (int)(img.Height * scale));
            int x = area.X + (area.Width - w) / 2;
            int y = area.Y + (area.Height - h) / 2;
            var dest = new Rectangle(x, y, w, h);

            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(img, dest);
            ControlPaint.DrawBorder(e.Graphics, Rectangle.Inflate(dest, 1, 1), Color.FromArgb(60, 60, 64), ButtonBorderStyle.Solid);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _image?.Dispose();
            base.Dispose(disposing);
        }
    }
}
