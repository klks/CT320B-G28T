using CT320B.LabelDesigner.Core.Model;
using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// Label setup: edit the page's physical width/height and gap. Values are shown/entered in the active
/// measurement unit (Phase 14d) but the model stays in millimetres — <see cref="WidthMm"/> etc. convert
/// back. Returns <see cref="DialogResult.OK"/> with the chosen values; the caller applies + refreshes.
/// </summary>
public sealed class LabelSetupForm : Form
{
    private readonly MeasurementUnit _unit;
    private readonly NumericUpDown _width;
    private readonly NumericUpDown _height;
    private readonly NumericUpDown _gap;

    /// <summary>Chosen label width in mm (valid after OK).</summary>
    public float WidthMm => UnitFormat.ToMm((float)_width.Value, _unit);

    /// <summary>Chosen label height in mm (valid after OK).</summary>
    public float HeightMm => UnitFormat.ToMm((float)_height.Value, _unit);

    /// <summary>Chosen inter-label gap in mm (valid after OK).</summary>
    public float GapMm => UnitFormat.ToMm((float)_gap.Value, _unit);

    public LabelSetupForm(LabelDocument document, MeasurementUnit unit = MeasurementUnit.Millimeters)
    {
        ArgumentNullException.ThrowIfNull(document);
        _unit = unit;
        _width = Spinner(1, 200);
        _height = Spinner(1, 500);
        _gap = Spinner(0, 50);

        Text = Loc.T("LabelSetup");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(270, 168);
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;

        _width.Value = Clamp(_width, (decimal)UnitFormat.ToDisplay(document.WidthMm, unit));
        _height.Value = Clamp(_height, (decimal)UnitFormat.ToDisplay(document.HeightMm, unit));
        _gap.Value = Clamp(_gap, (decimal)UnitFormat.ToDisplay(document.GapMm, unit));

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 2, RowCount = 3, AutoSize = true, Padding = new Padding(12, 12, 12, 4),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        string u = UnitFormat.Suffix(unit);
        AddRow(grid, $"{Loc.T("Width")} ({u})", _width);
        AddRow(grid, $"{Loc.T("Height")} ({u})", _height);
        AddRow(grid, $"{Loc.T("Gap")} ({u})", _gap);

        var ok = new Button { Text = Loc.T("OK"), DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = Loc.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(buttons);
        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    // The min/max are in mm; convert to the display unit so the spinner range matches the shown values.
    private NumericUpDown Spinner(decimal minMm, decimal maxMm) => new()
    {
        Minimum = (decimal)UnitFormat.ToDisplay((float)minMm, _unit),
        Maximum = (decimal)UnitFormat.ToDisplay((float)maxMm, _unit),
        DecimalPlaces = UnitFormat.Decimals(_unit),
        Increment = _unit == MeasurementUnit.Inches ? 0.1m : 1m,
        Width = 78, TextAlign = HorizontalAlignment.Right, Anchor = AnchorStyles.Right,
    };

    private static decimal Clamp(NumericUpDown n, decimal v) => Math.Clamp(v, n.Minimum, n.Maximum);

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 8, 8) });
        grid.Controls.Add(control);
    }
}
