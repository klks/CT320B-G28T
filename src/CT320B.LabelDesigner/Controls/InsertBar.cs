using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// A vertical tool bar of "insert element" buttons down the left edge (the layout the Clabel app
/// uses). Each item is an icon + label; the host wires the click actions.
/// </summary>
public sealed class InsertBar : UserControl
{
    private readonly FlowLayoutPanel _list;

    public InsertBar()
    {
        Dock = DockStyle.Left;
        Width = 150;
        BackColor = Color.White;

        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(5, 4, 5, 4), BackColor = Color.White,
        };
        Controls.Add(_list);
        Controls.Add(new Label
        {
            Text = Loc.T("Insert"), Dock = DockStyle.Top, Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
            ForeColor = Color.DimGray, BackColor = Color.FromArgb(238, 238, 240), Padding = new Padding(6, 4, 0, 4),
        });
    }

    /// <summary>Adds a full-width icon+text tool button.</summary>
    public void AddItem(string text, Image icon, Action onClick)
    {
        var b = new Button
        {
            Text = "  " + text, Image = icon, TextImageRelation = TextImageRelation.ImageBeforeText,
            ImageAlign = ContentAlignment.MiddleLeft, TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat, Width = 132, Height = 30, Margin = new Padding(0, 0, 0, 2),
            Font = new Font("Segoe UI", 9f),
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 241, 251);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(204, 228, 247);
        b.Click += (_, _) => onClick();
        _list.Controls.Add(b);
    }

    /// <summary>Adds a thin separator between groups of tools.</summary>
    public void AddSeparator() => _list.Controls.Add(new Label
    {
        Height = 1, Width = 132, BorderStyle = BorderStyle.Fixed3D, Margin = new Padding(2, 4, 2, 4),
    });
}
