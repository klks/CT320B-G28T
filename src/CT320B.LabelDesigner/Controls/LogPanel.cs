using CT320B.LabelDesigner.Services;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// A collapsible event-log panel (Phase 14a) fed by <see cref="AppLog"/>: a timestamped, colour-coded
/// list of recent messages with a small header (title + Clear + hide). Docked at the bottom of the
/// shell; toggled from the View ribbon. Marshals <see cref="AppLog.EntryAdded"/> onto the UI thread.
/// </summary>
public sealed class LogPanel : Panel
{
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.None,
    };

    /// <summary>Raised when the user clicks the hide (×) button.</summary>
    public event Action? HideRequested;

    public LogPanel()
    {
        Dock = DockStyle.Bottom;
        Height = 150;
        _list.Columns.Add("Time", 70);
        _list.Columns.Add("Level", 64);
        _list.Columns.Add("Message", 700);

        Controls.Add(_list);
        Controls.Add(BuildHeader());

        foreach (LogEntry e in AppLog.Snapshot()) Append(e);
        ScrollToEnd();
        AppLog.EntryAdded += OnEntryAdded;
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = Color.FromArgb(238, 238, 240) };
        var title = new Label { Text = "Log", Dock = DockStyle.Left, AutoSize = true, Padding = new Padding(6, 4, 0, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
        var hide = new Button { Text = "×", Dock = DockStyle.Right, Width = 28, FlatStyle = FlatStyle.Flat };
        var clear = new Button { Text = "Clear", Dock = DockStyle.Right, Width = 56, FlatStyle = FlatStyle.Flat };
        hide.FlatAppearance.BorderSize = 0;
        clear.FlatAppearance.BorderSize = 0;
        hide.Click += (_, _) => HideRequested?.Invoke();
        clear.Click += (_, _) => _list.Items.Clear();
        header.Controls.Add(title);
        header.Controls.Add(clear);
        header.Controls.Add(hide);
        return header;
    }

    private void OnEntryAdded(LogEntry entry)
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) BeginInvoke(() => { Append(entry); ScrollToEnd(); });
        else if (IsHandleCreated) { Append(entry); ScrollToEnd(); }
    }

    private void Append(LogEntry e)
    {
        var item = new ListViewItem(e.Time.ToString("HH:mm:ss"));
        item.SubItems.Add(e.Severity.ToString());
        item.SubItems.Add(e.Message);
        item.ForeColor = e.Severity switch
        {
            LogSeverity.Error => Color.FromArgb(170, 20, 20),
            LogSeverity.Warning => Color.FromArgb(150, 100, 0),
            LogSeverity.Success => Color.FromArgb(20, 110, 40),
            _ => SystemColors.ControlText,
        };
        _list.Items.Add(item);
    }

    private void ScrollToEnd()
    {
        if (_list.Items.Count > 0) _list.EnsureVisible(_list.Items.Count - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) AppLog.EntryAdded -= OnEntryAdded;
        base.Dispose(disposing);
    }
}
