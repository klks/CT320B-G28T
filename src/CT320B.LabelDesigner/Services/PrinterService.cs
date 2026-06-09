using CT320B.UsbApi;
using CT320B.UsbApi.Enumeration;
using Microsoft.Extensions.Logging;

namespace CT320B.LabelDesigner.Services;

/// <summary>Connection lifecycle of <see cref="PrinterService"/>.</summary>
public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}

/// <summary>
/// Owns the <see cref="CT320BPrinter"/> lifecycle for the app: async USB enumeration / Bluetooth
/// discovery and connect, off the UI thread (Decision D5), plus USB hot-plug auto-reopen. Pure
/// (no WinForms dependency) so it can be reused and tested; the UI subscribes to
/// <see cref="StatusChanged"/> / <see cref="ErrorOccurred"/> and marshals to its own thread.
/// </summary>
public sealed class PrinterService : IDisposable
{
    private CT320BPrinter? _printer;
    private readonly ILogger? _logger;

    /// <summary>Creates the service, optionally forwarding the printer library's diagnostics to
    /// <paramref name="logger"/> (the app wires an <see cref="AppLoggerProvider"/> logger here).</summary>
    public PrinterService(ILogger? logger = null) => _logger = logger;

    /// <summary>Current connection status.</summary>
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    /// <summary>True when a printer is connected.</summary>
    public bool IsConnected => Status == ConnectionStatus.Connected && _printer is not null;

    /// <summary>True when the connection is up <i>and</i> the underlying transport is open. After a
    /// USB unplug this goes false until hot-plug reopen restores it.</summary>
    public bool IsLinkUp => _printer?.Transport.IsOpen == true;

    /// <summary>Description of the connected device (for the status label), or null.</summary>
    public string? ConnectedDescription { get; private set; }

    /// <summary>The live printer, or null when disconnected. Used by print/status panels.</summary>
    public CT320BPrinter? Printer => _printer;

    /// <summary>Raised whenever <see cref="Status"/> changes. May fire on a background thread.</summary>
    public event Action<ConnectionStatus>? StatusChanged;

    /// <summary>Raised with a user-facing error message (for toasts). May fire on a background thread.</summary>
    public event Action<string>? ErrorOccurred;

    // --- Enumeration / discovery (off the UI thread) ---

    /// <summary>Enumerates present USB printer interfaces.</summary>
    public Task<IReadOnlyList<UsbPrinterInfo>> EnumerateUsbAsync() =>
        Task.Run(UsbPrinterEnumerator.Enumerate);

    /// <summary>Discovers Bluetooth devices. <paramref name="issueInquiry"/> true does a live radio
    /// scan (slow); false returns only remembered/paired/connected devices (fast).</summary>
    public Task<IReadOnlyList<BluetoothPrinterInfo>> ScanBluetoothAsync(
        bool issueInquiry, int timeoutSeconds = 10) =>
        Task.Run(() => BluetoothDiscovery.Discover(issueInquiry, timeoutSeconds));

    // --- Connect / disconnect ---

    /// <summary>Connects to the selected device (USB or Bluetooth) off the UI thread, replacing any
    /// existing connection. Errors are surfaced via <see cref="ErrorOccurred"/>; returns true on success.</summary>
    public async Task<bool> ConnectAsync(DeviceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Disconnect();
        SetStatus(ConnectionStatus.Connecting);
        try
        {
            switch (target)
            {
                case UsbDeviceTarget usb:
                {
                    CT320BPrinter printer = await Task.Run(() => CT320BPrinter.OpenUsb(usb.Info.DevicePath, _logger))
                        .ConfigureAwait(false);
                    printer.EnableUsbHotPlugReopen();   // auto-reopen on replug (DLL's OnReopenTimer)
                    _printer = printer;
                    ConnectedDescription = usb.Display;
                    break;
                }
                case BluetoothDeviceTarget bt:
                {
                    CT320BPrinter printer = await Task.Run(() => CT320BPrinter.OpenBluetooth(bt.Info.Address, _logger))
                        .ConfigureAwait(false);
                    _printer = printer;
                    ConnectedDescription = bt.Display;
                    break;
                }
                default:
                    throw new NotSupportedException($"Unknown device target '{target.GetType().Name}'.");
            }
            SetStatus(ConnectionStatus.Connected);
            return true;
        }
        catch (Exception ex)
        {
            ConnectedDescription = null;
            SetStatus(ConnectionStatus.Failed);
            ErrorOccurred?.Invoke(DescribeConnectError(target, ex));
            return false;
        }
    }

    // --- Commands / queries (off the UI thread, Decision D5) ---

    /// <summary>Runs a fire-and-forget printer command (e.g. self-test, calibrate) off the UI thread.
    /// Throws <see cref="InvalidOperationException"/> if not connected; transport/IO errors propagate
    /// through the returned task so the caller can report them.</summary>
    public Task ExecuteAsync(Action<CT320BPrinter> command)
    {
        ArgumentNullException.ThrowIfNull(command);
        CT320BPrinter printer = _printer ?? throw new InvalidOperationException("Not connected to a printer.");
        return Task.Run(() => command(printer));
    }

    /// <summary>Runs a printer query that returns a value (e.g. read RFID / status / raw response) off
    /// the UI thread. Throws <see cref="InvalidOperationException"/> if not connected; other errors
    /// propagate through the returned task.</summary>
    public Task<T> QueryAsync<T>(Func<CT320BPrinter, T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        CT320BPrinter printer = _printer ?? throw new InvalidOperationException("Not connected to a printer.");
        return Task.Run(() => query(printer));
    }

    /// <summary>Disconnects and disposes the current printer (idempotent).</summary>
    public void Disconnect()
    {
        if (_printer is null)
        {
            if (Status != ConnectionStatus.Disconnected) SetStatus(ConnectionStatus.Disconnected);
            return;
        }
        _printer.Dispose();
        _printer = null;
        ConnectedDescription = null;
        SetStatus(ConnectionStatus.Disconnected);
    }

    private void SetStatus(ConnectionStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private static string DescribeConnectError(DeviceTarget target, Exception ex)
    {
        // The unpaired-Bluetooth case fails deep in the socket connect (WSA 10051); give a clear hint.
        if (target is BluetoothDeviceTarget { Info.Authenticated: false })
            return $"Couldn't connect over Bluetooth — pair the printer in Windows Bluetooth settings first. ({ex.Message})";
        return $"Connection failed: {ex.Message}";
    }

    public void Dispose() => Disconnect();
}
