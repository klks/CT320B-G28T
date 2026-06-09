using CT320B.UsbApi.Enumeration;
using CT320B.UsbApi.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CT320B.UsbApi.Bluetooth;

/// <summary>Connection lifecycle states reported by <see cref="BluetoothPrinterClient"/>
/// (the modern equivalent of the DLL's connect-callback state argument).</summary>
public enum BluetoothConnectionState
{
    Connecting,
    Connected,
    Failed,
    Disconnected,
}

/// <summary>
/// An <c>async</c>/event-driven Bluetooth client — the modern equivalent of the DLL's
/// <c>BluetoothService</c> (scan/connect/recv via a hidden window + C callbacks). Discovery and
/// connection run on the thread pool and surface results through .NET events instead of
/// <c>discoverBluetoothCallback</c> / window messages:
/// <list type="bullet">
/// <item><see cref="DeviceDiscovered"/> ⇿ <c>discoverBluetoothCallback</c> (per device);</item>
/// <item><see cref="ScanCompleted"/> ⇿ the <c>0x467</c> scan-complete message;</item>
/// <item><see cref="ConnectionStateChanged"/> ⇿ the connect-thread state callback;</item>
/// <item><see cref="DataReceived"/> ⇿ <c>recvDataCallback</c> — but actually wired: the original
/// DLL is send-only (no <c>recv</c>), so <see cref="StartReceiveLoop"/> is a new full-duplex
/// capability the managed port adds (the RFCOMM channel supports it).</item>
/// </list>
/// Once connected, <see cref="Printer"/> exposes the full high-level TSPL/CPCL API.
/// </summary>
public sealed class BluetoothPrinterClient : IDisposable
{
    private readonly ILogger _logger;
    private RfcommTransport? _transport;
    private CT320BPrinter? _printer;
    private CancellationTokenSource? _recvCts;
    private Task? _recvTask;

    public BluetoothPrinterClient(ILogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    /// <summary>Raised once per device found during a scan.</summary>
    public event Action<BluetoothPrinterInfo>? DeviceDiscovered;

    /// <summary>Raised when a scan finishes (after the last <see cref="DeviceDiscovered"/>).</summary>
    public event Action? ScanCompleted;

    /// <summary>Raised as the connection moves through its lifecycle states.</summary>
    public event Action<BluetoothConnectionState>? ConnectionStateChanged;

    /// <summary>Raised with each chunk of bytes received while a receive loop is running.</summary>
    public event Action<byte[]>? DataReceived;

    /// <summary>True once an RFCOMM connection is established.</summary>
    public bool IsConnected => _transport?.IsOpen == true;

    /// <summary>The high-level printer over the live connection, or null if not connected.</summary>
    public CT320BPrinter? Printer => _printer;

    /// <summary>
    /// Asynchronously discovers Bluetooth devices, raising <see cref="DeviceDiscovered"/> per match
    /// and <see cref="ScanCompleted"/> at the end. Returns the full list.
    /// </summary>
    public Task<IReadOnlyList<BluetoothPrinterInfo>> ScanAsync(
        bool issueInquiry = true, int timeoutSeconds = 10,
        Func<BluetoothPrinterInfo, bool>? filter = null, CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<BluetoothPrinterInfo>>(() =>
        {
            // BluetoothDiscovery.Discover's filter hook lets us stream each device as it's seen,
            // mirroring the DLL's per-device callback.
            IReadOnlyList<BluetoothPrinterInfo> devices = BluetoothDiscovery.Discover(
                issueInquiry, timeoutSeconds,
                d =>
                {
                    bool keep = filter is null || filter(d);
                    if (keep) DeviceDiscovered?.Invoke(d);
                    return keep;
                });
            ScanCompleted?.Invoke();
            return devices;
        }, cancellationToken);
    }

    /// <summary>Asynchronously connects to a printer by 48-bit address.</summary>
    public Task ConnectAsync(ulong address, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            Disconnect();
            ConnectionStateChanged?.Invoke(BluetoothConnectionState.Connecting);
            try
            {
                var transport = new RfcommTransport(address);
                transport.Connect();
                _transport = transport;
                _printer = new CT320BPrinter(transport, ownsTransport: false, _logger);
                _logger.LogInformation("Connected to Bluetooth printer {Address}.", RfcommTransport.FormatAddress(address));
                ConnectionStateChanged?.Invoke(BluetoothConnectionState.Connected);
            }
            catch
            {
                ConnectionStateChanged?.Invoke(BluetoothConnectionState.Failed);
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>Connects to "AA:BB:CC:DD:EE:FF".</summary>
    public Task ConnectAsync(string address, CancellationToken cancellationToken = default) =>
        ConnectAsync(RfcommTransport.ParseAddress(address), cancellationToken);

    /// <summary>Scans, then connects to the first device whose name contains
    /// <paramref name="name"/> (the async equivalent of <c>connectBluetoothByName</c>).</summary>
    public async Task ConnectByNameAsync(
        string name, bool issueInquiry = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        IReadOnlyList<BluetoothPrinterInfo> devices = await ScanAsync(
            issueInquiry,
            filter: d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        BluetoothPrinterInfo device = devices.Count > 0
            ? devices[0]
            : throw new InvalidOperationException($"No Bluetooth device matching '{name}'.");
        await ConnectAsync(device.Address, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Scans, then connects to the device at <paramref name="index"/> of the results
    /// (the async equivalent of <c>connectBluetoothByIndex</c>).</summary>
    public async Task ConnectByIndexAsync(
        int index, bool issueInquiry = true, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BluetoothPrinterInfo> devices = await ScanAsync(
            issueInquiry, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (index < 0 || index >= devices.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Only {devices.Count} device(s) found.");
        await ConnectAsync(devices[index].Address, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asynchronously sends raw bytes over the connection (= <c>sendData</c>).</summary>
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        RfcommTransport transport = _transport ?? throw new InvalidOperationException("Not connected.");
        return Task.Run(() =>
        {
            if (transport.Write(data.Span) < 0)
                throw new IOException("Bluetooth send failed.");
        }, cancellationToken);
    }

    /// <summary>
    /// Starts a background receive loop that raises <see cref="DataReceived"/> for each chunk read
    /// from the socket (a full-duplex capability beyond the original send-only DLL). Idempotent.
    /// </summary>
    public void StartReceiveLoop(int bufferSize = 2048, int readTimeoutMs = 1000)
    {
        RfcommTransport transport = _transport ?? throw new InvalidOperationException("Not connected.");
        if (_recvTask is not null) return;

        _recvCts = new CancellationTokenSource();
        CancellationToken ct = _recvCts.Token;
        _recvTask = Task.Run(() =>
        {
            var buffer = new byte[bufferSize];
            while (!ct.IsCancellationRequested && transport.IsOpen)
            {
                int read = transport.Read(buffer, readTimeoutMs);
                if (read > 0)
                {
                    var chunk = new byte[read];
                    Array.Copy(buffer, chunk, read);
                    DataReceived?.Invoke(chunk);
                }
                // read <= 0 is a timeout / no data; loop until cancelled or disconnected.
            }
        }, ct);
    }

    /// <summary>Stops the receive loop started by <see cref="StartReceiveLoop"/>.</summary>
    public void StopReceiveLoop()
    {
        _recvCts?.Cancel();
        try { _recvTask?.Wait(1500); } catch (AggregateException) { /* loop torn down */ }
        _recvCts?.Dispose();
        _recvCts = null;
        _recvTask = null;
    }

    /// <summary>Closes the connection (= <c>disconnectBluetooth</c>).</summary>
    public void Disconnect()
    {
        StopReceiveLoop();
        if (_transport is not null)
        {
            _transport.Dispose();
            _transport = null;
            _printer = null;
            ConnectionStateChanged?.Invoke(BluetoothConnectionState.Disconnected);
        }
    }

    public void Dispose() => Disconnect();
}
