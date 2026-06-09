using System.ComponentModel;
using CT320B.LabelDesigner.Services;
using CT320B.UsbApi.Enumeration;

namespace CT320B.LabelDesigner.Controls;

/// <summary>
/// A compact printer bar along the bottom (the "task area", matching the Clabel app): a device
/// dropdown (USB + remembered Bluetooth), refresh, connect/disconnect, a status light, and a label
/// for the current label size. Backed by the shared <see cref="PrinterService"/>.
/// </summary>
public sealed class DeviceStatusBar : UserControl
{
    private readonly PrinterService _service;
    private readonly StatusLight _light = new() { Margin = new Padding(6, 6, 4, 0) };
    private readonly ComboBox _devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250, Margin = new Padding(2, 3, 4, 0) };
    private readonly Button _refresh = new() { Text = Loc.T("Refresh"), AutoSize = true, Margin = new Padding(0, 2, 4, 0) };
    private readonly Button _connect = new() { Text = Loc.T("Connect"), AutoSize = true, Margin = new Padding(0, 2, 4, 0) };
    private readonly Button _disconnect = new() { Text = Loc.T("Disconnect"), AutoSize = true, Margin = new Padding(0, 2, 4, 0) };
    private readonly Label _status = new() { Text = Loc.T("Disconnected"), AutoSize = true, Margin = new Padding(4, 7, 0, 0) };
    private readonly Label _info = new() { Dock = DockStyle.Right, AutoSize = false, Width = 200, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.DimGray, Padding = new Padding(0, 0, 8, 0) };
    private readonly ComboBox _lang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 132, Margin = new Padding(2, 3, 0, 0), DisplayMember = nameof(LanguageInfo.Name) };
    private readonly Button _langFolder = new() { Text = "…", Width = 26, Margin = new Padding(2, 2, 8, 0) };
    private bool _busy;
    private bool _loadingLang;

    /// <summary>Raised when the user picks a UI language (the chosen language's culture code).</summary>
    public event Action<string>? LanguageSelected;

    public DeviceStatusBar(PrinterService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Dock = DockStyle.Bottom;
        Height = 30;
        BackColor = Color.FromArgb(240, 240, 242);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Padding = new Padding(2, 0, 0, 0), BackColor = Color.Transparent,
        };
        flow.Controls.Add(_light);
        flow.Controls.Add(new Label { Text = Loc.T("PrinterColon"), AutoSize = true, Margin = new Padding(2, 7, 2, 0) });
        flow.Controls.Add(_devices);
        flow.Controls.Add(_refresh);
        flow.Controls.Add(_connect);
        flow.Controls.Add(_disconnect);
        flow.Controls.Add(_status);

        // Dock order: Fill first, then the right-docked items (last added sits furthest right). The
        // language picker goes at the very end (far bottom-right); the size info to its left.
        Controls.Add(flow);
        Controls.Add(_info);
        Controls.Add(BuildLanguagePanel());

        _refresh.Click += async (_, _) => await RefreshDevicesAsync();
        _connect.Click += async (_, _) => await ConnectAsync();
        _disconnect.Click += (_, _) => _service.Disconnect();
        _devices.SelectedIndexChanged += (_, _) => UpdateButtons();

        _service.StatusChanged += _ => Ui(UpdateUi);
        _service.ErrorOccurred += msg => Ui(() => _status.Text = msg);

        Load += async (_, _) => { UpdateUi(); await RefreshDevicesAsync(); };
    }

    /// <summary>Right-aligned info text (e.g. the current label size).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden), Browsable(false)]
    public string Info { set => _info.Text = value; }

    // The language picker, docked at the far right: 🌐 + dropdown + a button that opens the lang folder.
    private Control BuildLanguagePanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            BackColor = Color.Transparent, Margin = new Padding(0), Padding = new Padding(4, 0, 6, 0),
        };
        panel.Controls.Add(new Label { Text = "🌐", AutoSize = true, Margin = new Padding(0, 6, 2, 0) });
        panel.Controls.Add(_lang);
        panel.Controls.Add(_langFolder);
        InitLanguagePicker();
        return panel;
    }

    // Fills the language dropdown (built-ins + any user files), selects the active one, and wires changes.
    private void InitLanguagePicker()
    {
        _loadingLang = true;
        foreach (LanguageInfo l in Loc.Available) _lang.Items.Add(l);
        for (int i = 0; i < _lang.Items.Count; i++)
            if (_lang.Items[i] is LanguageInfo l && string.Equals(l.Code, Loc.Current.Code, StringComparison.OrdinalIgnoreCase))
            { _lang.SelectedIndex = i; break; }
        if (_lang.SelectedIndex < 0 && _lang.Items.Count > 0) _lang.SelectedIndex = 0;
        _loadingLang = false;

        var tip = new ToolTip();
        tip.SetToolTip(_lang, Loc.T("Language"));
        tip.SetToolTip(_langFolder, Loc.T("AddLanguage"));
        _lang.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingLang && _lang.SelectedItem is LanguageInfo li) LanguageSelected?.Invoke(li.Code);
        };
        _langFolder.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LangDir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppPaths.LangDir) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { }
        };
    }

    private async Task RefreshDevicesAsync()
    {
        if (_busy) return;
        SetBusy(true);
        object? previous = _devices.SelectedItem;
        try
        {
            var targets = new List<DeviceTarget>();
            IReadOnlyList<UsbPrinterInfo> usb = await _service.EnumerateUsbAsync();
            targets.AddRange(usb.Select(i => (DeviceTarget)new UsbDeviceTarget(i)));
            IReadOnlyList<BluetoothPrinterInfo> bt = await _service.ScanBluetoothAsync(issueInquiry: false);
            targets.AddRange(bt.Select(i => (DeviceTarget)new BluetoothDeviceTarget(i)));

            _devices.BeginUpdate();
            _devices.Items.Clear();
            _devices.Items.AddRange(targets.Cast<object>().ToArray());
            _devices.EndUpdate();

            if (previous is DeviceTarget prev)
            {
                int idx = _devices.Items.Cast<object>().ToList()
                    .FindIndex(o => o.ToString() == prev.Display);
                if (idx >= 0) _devices.SelectedIndex = idx;
            }
            if (_devices.SelectedIndex < 0 && _devices.Items.Count > 0) _devices.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _status.Text = Loc.F("ScanFailed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ConnectAsync()
    {
        if (_busy || _devices.SelectedItem is not DeviceTarget target) return;
        SetBusy(true);
        try { await _service.ConnectAsync(target); }
        finally { SetBusy(false); }
    }

    private void UpdateUi()
    {
        switch (_service.Status)
        {
            case ConnectionStatus.Connected when _service.IsLinkUp:
                _light.SetColor(Color.ForestGreen); _status.Text = _service.ConnectedDescription ?? Loc.T("Connected"); break;
            case ConnectionStatus.Connected:
                _light.SetColor(Color.Goldenrod); _status.Text = Loc.T("ConnectedLinkDown"); break;
            case ConnectionStatus.Connecting:
                _light.SetColor(Color.Goldenrod); _status.Text = Loc.T("Connecting"); break;
            case ConnectionStatus.Failed:
                _light.SetColor(Color.Firebrick); _status.Text = Loc.T("ConnectionFailed"); break;
            default:
                _light.SetColor(Color.Gray); _status.Text = Loc.T("Disconnected"); break;
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool connected = _service.IsConnected;
        _connect.Enabled = !_busy && !connected && _devices.SelectedItem is DeviceTarget;
        _disconnect.Enabled = connected;
        _refresh.Enabled = !_busy;
        _devices.Enabled = !_busy && !connected;
    }

    private void SetBusy(bool busy) { _busy = busy; UpdateButtons(); }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (IsHandleCreated && InvokeRequired) BeginInvoke(action); else action();
    }
}
