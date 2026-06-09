using System.Text;
using CT320B.LabelDesigner.Services;
using CT320B.UsbApi;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// The printer status &amp; control panel (Phase 7): calibration/control commands (self-test, gap /
/// black-line detect, auto-detect, initialize), live density/speed, status reads (RFID, print mode,
/// print memory), and a raw command console. Every printer call runs off the UI thread via
/// <see cref="PrinterService.ExecuteAsync"/> / <see cref="PrinterService.QueryAsync"/>; results and
/// errors are appended to the console log.
/// </summary>
public sealed class ControlPanelForm : Form
{
    private readonly PrinterService _service;
    private readonly NumericUpDown _density = new() { Minimum = 0, Maximum = 15, Value = 8, Width = 56 };
    private readonly NumericUpDown _speed = new() { Minimum = 1, Maximum = 14, Value = 5, Width = 56 };
    private readonly TextBox _rawInput = new() { Width = 250, Anchor = AnchorStyles.Left | AnchorStyles.Right };
    private readonly CheckBox _appendCrlf = new() { Text = Loc.T("AppendCrlf"), Checked = true, AutoSize = true };
    private readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
        Font = new Font("Consolas", 9f), BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.Gainsboro,
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(6, 0, 0, 0), BackColor = Color.FromArgb(238, 238, 240),
    };
    private readonly List<Control> _hardwareControls = [];

    public ControlPanelForm(PrinterService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));

        Text = Loc.T("PrinterControl");
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 600);
        MinimumSize = new Size(440, 480);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // calibration
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // settings
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // raw input
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // log
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildControlGroup(), 0, 0);
        root.Controls.Add(BuildSettingsGroup(), 0, 1);
        root.Controls.Add(BuildStatusGroup(), 0, 2);
        root.Controls.Add(BuildRawGroup(), 0, 3);
        root.Controls.Add(BuildLogGroup(), 0, 4);

        Controls.Add(root);
        Controls.Add(_status);

        _service.StatusChanged += OnServiceStatusChanged;
        ReflectConnection();
    }

    private GroupBox BuildControlGroup()
    {
        var flow = NewFlow();
        flow.Controls.Add(HwButton(Loc.T("SelfTest"), p => p.SelfTest(), Loc.T("SelfTestSent")));
        flow.Controls.Add(HwButton(Loc.T("CalibrateGap"), p => p.GapDetect(), Loc.T("GapCalSent")));
        flow.Controls.Add(HwButton(Loc.T("CalibrateBlackLine"), p => p.BlineDetect(), Loc.T("BlackLineSent")));
        flow.Controls.Add(HwButton(Loc.T("AutoDetect"), p => p.AutoDetect(), Loc.T("AutoDetectSent")));
        flow.Controls.Add(HwButton(Loc.T("Initialize"), p => p.InitialPrinter(), Loc.T("PrinterInit")));
        return Group(Loc.T("CalibrationControl"), flow);
    }

    private GroupBox BuildSettingsGroup()
    {
        var flow = NewFlow();
        flow.Controls.Add(new Label { Text = Loc.T("Density"), AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        flow.Controls.Add(_density);
        flow.Controls.Add(new Label { Text = Loc.T("Speed"), AutoSize = true, Margin = new Padding(12, 8, 3, 3) });
        flow.Controls.Add(_speed);
        flow.Controls.Add(HwButton(Loc.T("Apply"), p =>
        {
            int d = 0; int s = 0;
            Invoke(() => { d = (int)_density.Value; s = (int)_speed.Value; });
            p.SetDensity(d);
            p.SetSpeed(s);
        }, Loc.T("DensitySpeedApplied")));
        return Group(Loc.T("PrintSettings"), flow);
    }

    private GroupBox BuildStatusGroup()
    {
        var flow = NewFlow();
        flow.Controls.Add(HwQueryButton(Loc.T("ReadRfid"), p => p.ReadRfidData(),
            v => v is null ? Loc.T("RfidNoReply") : Loc.F("RfidBytes", v.Length, Hex(v))));
        flow.Controls.Add(HwQueryButton(Loc.T("ReadPrintMode"), p => p.ReadPrintMode(),
            v => v is null ? Loc.T("PrintModeNoReply") : Loc.F("PrintModeVal", v, v)));
        flow.Controls.Add(HwQueryButton(Loc.T("ReadPrintMemory"), p => p.ReadPrintMemory(),
            v => v is null ? Loc.T("PrintMemNoReply") : Loc.F("PrintMemVal", v, v)));
        return Group(Loc.T("Status"), flow);
    }

    private GroupBox BuildRawGroup()
    {
        var flow = NewFlow();
        _hardwareControls.Add(_rawInput);
        _rawInput.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendRaw(); } };
        flow.Controls.Add(_rawInput);
        flow.Controls.Add(_appendCrlf);
        flow.Controls.Add(HwButton(Loc.T("Send"), _ => { }, null, onClickOverride: (_, _) => SendRaw()));
        flow.Controls.Add(HwButton(Loc.T("ReadResponse"), _ => { }, null, onClickOverride: (_, _) => ReadResponse()));
        return Group(Loc.T("RawConsole"), flow);
    }

    private GroupBox BuildLogGroup()
    {
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 0) };
        host.Controls.Add(_log);
        var clear = new Button { Text = Loc.T("ClearLog"), Dock = DockStyle.Bottom, Height = 26 };
        clear.Click += (_, _) => _log.Clear();
        host.Controls.Add(clear);
        return Group(Loc.T("ConsoleLog"), host, fill: true);
    }

    // --- command plumbing ---

    private Button HwButton(string text, Action<CT320BPrinter> command, string? okMessage,
        EventHandler? onClickOverride = null)
    {
        var btn = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        if (onClickOverride is not null)
            btn.Click += onClickOverride;
        else
            btn.Click += async (_, _) => await RunAsync(text, command, okMessage);
        _hardwareControls.Add(btn);
        return btn;
    }

    private Button HwQueryButton<T>(string text, Func<CT320BPrinter, T> query, Func<T, string> describe)
    {
        var btn = new Button { Text = text, AutoSize = true, Margin = new Padding(3) };
        btn.Click += async (_, _) =>
        {
            SetStatus($"{text}…");
            try
            {
                T result = await _service.QueryAsync(query);
                string line = describe(result);
                Append(line);
                SetStatus(line);
            }
            catch (Exception ex) { ReportError(text, ex); }
        };
        _hardwareControls.Add(btn);
        return btn;
    }

    private async Task RunAsync(string label, Action<CT320BPrinter> command, string? okMessage)
    {
        SetStatus($"{label}…");
        try
        {
            await _service.ExecuteAsync(command);
            string msg = okMessage ?? $"{label} done.";
            Append(msg);
            SetStatus(msg);
        }
        catch (Exception ex) { ReportError(label, ex); }
    }

    private async void SendRaw()
    {
        string text = _rawInput.Text;
        if (text.Length == 0) return;
        if (_appendCrlf.Checked) text += "\r\n";
        byte[] bytes = Encoding.Latin1.GetBytes(text);
        try
        {
            await _service.ExecuteAsync(p => p.SendRaw(bytes));
            Append($"TX: {Hex(bytes)}");
            SetStatus(Loc.F("SentBytes", bytes.Length));
        }
        catch (Exception ex) { ReportError(Loc.T("Send"), ex); }
    }

    private async void ReadResponse()
    {
        SetStatus(Loc.T("ReadingResponse"));
        try
        {
            byte[] data = await _service.QueryAsync(p =>
            {
                var buffer = new byte[2048];
                int n = p.Transport.Read(buffer, 800);
                return n > 0 ? buffer[..n] : [];
            });
            if (data.Length == 0) { Append(Loc.T("RxNoData")); SetStatus(Loc.T("NoResponse")); return; }
            Append($"RX ({data.Length} bytes): {Hex(data)}");
            Append($"     {Ascii(data)}");
            SetStatus(Loc.F("ReadBytes", data.Length));
        }
        catch (Exception ex) { ReportError(Loc.T("Read"), ex); }
    }

    // --- helpers ---

    private void OnServiceStatusChanged(ConnectionStatus _)
    {
        if (IsHandleCreated) BeginInvoke(ReflectConnection);
    }

    private void ReflectConnection()
    {
        bool up = _service.IsConnected;
        foreach (Control c in _hardwareControls) c.Enabled = up;
        if (!up) SetStatus(Loc.T("NotConnectedBar"));
        else if (_status.Text.Length == 0 || _status.Text == Loc.T("NotConnectedBar"))
            SetStatus(Loc.F("ConnectedColon", _service.ConnectedDescription ?? ""));
    }

    private void SetStatus(string text) => _status.Text = text;

    private void Append(string line) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");

    private void ReportError(string label, Exception ex)
    {
        // Task.Run faults wrap the real exception in AggregateException only when accessed via .Result;
        // awaited tasks unwrap it, so ex is already the inner cause.
        string msg = $"{label} failed: {ex.Message}";
        Append(msg);
        SetStatus(msg);
    }

    private static string Hex(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length * 3);
        foreach (byte b in data) sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    private static string Ascii(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length);
        foreach (byte b in data) sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        return sb.ToString();
    }

    private static FlowLayoutPanel NewFlow() => new()
    {
        Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(4),
    };

    private static GroupBox Group(string title, Control content, bool fill = false)
    {
        var box = new GroupBox { Text = title, AutoSize = !fill, Dock = DockStyle.Fill, Padding = new Padding(6) };
        if (fill) box.AutoSize = false;
        content.Dock = DockStyle.Fill;
        box.Controls.Add(content);
        return box;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _service.StatusChanged -= OnServiceStatusChanged;
        base.Dispose(disposing);
    }
}
