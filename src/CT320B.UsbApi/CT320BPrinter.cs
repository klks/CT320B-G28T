using System.Drawing;
using CT320B.UsbApi.Enumeration;
using CT320B.UsbApi.Imaging;
using CT320B.UsbApi.Native;
using CT320B.UsbApi.Protocol.Cpcl;
using CT320B.UsbApi.Protocol.Status;
using CT320B.UsbApi.Protocol.Tspl;
using CT320B.UsbApi.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CT320B.UsbApi;

/// <summary>
/// High-level CT320B thermal-printer client — the managed equivalent of <c>USBDeviceService</c>'s
/// public surface, but transport-agnostic: it drives any <see cref="IPrinterTransport"/>, so the
/// same API prints over USB today and Bluetooth/RFCOMM once that transport lands.
///
/// Command methods build TSPL/CPCL bytes (byte-verified against the original DLL) and send them;
/// a failed transport write throws <see cref="IOException"/>.
/// </summary>
public sealed class CT320BPrinter : IDisposable
{
    private readonly IPrinterTransport _transport;
    private readonly bool _ownsTransport;
    private readonly ILogger _logger;
    private byte _rfidSeq;
    private DeviceNotificationWindow? _hotPlug;

    /// <summary>Wraps an already-open transport. The caller keeps ownership unless
    /// <paramref name="ownsTransport"/> is true. Pass an <paramref name="logger"/> for diagnostics.</summary>
    public CT320BPrinter(IPrinterTransport transport, bool ownsTransport = false, ILogger? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _ownsTransport = ownsTransport;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Opens the printer at a specific usbprint device path.</summary>
    public static CT320BPrinter OpenUsb(string devicePath, ILogger? logger = null)
    {
        var transport = new UsbPrintTransport(devicePath);
        transport.Open();
        logger?.LogInformation("Opened USB printer at {DevicePath}.", devicePath);
        return new CT320BPrinter(transport, ownsTransport: true, logger);
    }

    /// <summary>Enumerates USB printer interfaces and opens the first one (optionally filtered).</summary>
    public static CT320BPrinter OpenFirstUsb(Func<UsbPrinterInfo, bool>? match = null, ILogger? logger = null)
    {
        UsbPrinterInfo? info = UsbPrinterEnumerator.Enumerate().FirstOrDefault(match ?? (_ => true));
        if (info is null)
            throw new InvalidOperationException("No USB printer interface found.");
        return OpenUsb(info.DevicePath, logger);
    }

    /// <summary>Opens the printer over Bluetooth RFCOMM by 48-bit device address.</summary>
    public static CT320BPrinter OpenBluetooth(ulong deviceAddress, ILogger? logger = null)
    {
        var transport = new RfcommTransport(deviceAddress);
        transport.Connect();
        logger?.LogInformation("Connected to Bluetooth printer {Address}.", RfcommTransport.FormatAddress(deviceAddress));
        return new CT320BPrinter(transport, ownsTransport: true, logger);
    }

    /// <summary>Opens the printer over Bluetooth RFCOMM by "AA:BB:CC:DD:EE:FF" address.</summary>
    public static CT320BPrinter OpenBluetooth(string address, ILogger? logger = null) =>
        OpenBluetooth(RfcommTransport.ParseAddress(address), logger);

    /// <summary>Discovers Bluetooth printers (name starts "CT" by default) and opens the first.
    /// If <paramref name="autoPair"/> is set, attempts to pair an unpaired match first.</summary>
    public static CT320BPrinter OpenFirstBluetooth(
        Func<BluetoothPrinterInfo, bool>? match = null, bool issueInquiry = true,
        bool autoPair = false, ILogger? logger = null)
    {
        match ??= d => d.Name.StartsWith(BluetoothDiscovery.PrinterNamePrefix, StringComparison.OrdinalIgnoreCase);
        BluetoothPrinterInfo? device = BluetoothDiscovery.Discover(issueInquiry).FirstOrDefault(match);
        if (device is null)
            throw new InvalidOperationException("No Bluetooth printer found.");
        if (autoPair && !device.Authenticated)
            BluetoothDiscovery.TryPair(device);
        return OpenBluetooth(device.Address, logger);
    }

    /// <summary>Discovers and opens a Bluetooth printer whose name contains <paramref name="name"/>.</summary>
    public static CT320BPrinter OpenBluetoothByName(string name, bool issueInquiry = true, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return OpenFirstBluetooth(
            d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase), issueInquiry, logger: logger);
    }

    /// <summary>Opens the Bluetooth printer at the given index of a discovery scan.</summary>
    public static CT320BPrinter OpenBluetoothByIndex(int index, bool issueInquiry = true, ILogger? logger = null)
    {
        IReadOnlyList<BluetoothPrinterInfo> devices = BluetoothDiscovery.Discover(issueInquiry);
        if (index < 0 || index >= devices.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"Only {devices.Count} device(s) found.");
        return OpenBluetooth(devices[index].Address, logger);
    }

    /// <summary>The underlying transport (open state, raw I/O).</summary>
    public IPrinterTransport Transport => _transport;

    /// <summary>
    /// Enables USB hot-plug auto-reopen (the managed equivalent of the DLL's hidden window +
    /// <c>RegisterDeviceNotification</c> → <c>OnReopenTimer</c>): when a usbprint interface arrives
    /// and this printer's <see cref="UsbPrintTransport"/> is closed, it is re-opened automatically.
    /// No-op for non-USB transports. Call once; disposed with the printer.
    /// </summary>
    public void EnableUsbHotPlugReopen()
    {
        if (_transport is not UsbPrintTransport usb || _hotPlug is not null) return;
        _hotPlug = new DeviceNotificationWindow(SetupApi.GUID_DEVINTERFACE_USBPRINT);
        _hotPlug.DeviceChanged += change =>
        {
            if (change != DeviceChange.Arrival || usb.IsOpen) return;
            try
            {
                if (usb.Reopen())
                    _logger.LogInformation("Reopen usb success.");   // matches the DLL's log line
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "USB hot-plug reopen failed; will retry on next arrival.");
            }
        };
    }

    // --- Page setup ---
    public void SetPaperSize(float width, float height, string? unit = null) =>
        Send(TsplCommandBuilder.SetPrintPaperSize(width, height, unit));
    public void SetGap(float gap, float offset, string? unit = null) =>
        Send(TsplCommandBuilder.SetPrintPaperGap(gap, offset, unit));
    public void SetBlackLine(float height, float offset, string? unit = null) =>
        Send(TsplCommandBuilder.SetBlackLine(height, offset, unit));
    public void SetDirection(int x, int y) => Send(TsplCommandBuilder.SetPrintDirection(x, y));
    public void SetReference(int refX, int refY, int direction) =>
        Send(TsplCommandBuilder.SetPrintReference(refX, refY, direction));
    public void SetSpeed(float speed) => Send(TsplCommandBuilder.SetPrintSpeed(speed));
    public void SetDensity(int density) => Send(TsplCommandBuilder.SetPrintDensity(density));
    public void SetOffset(float offset, string? unit = null) =>
        Send(TsplCommandBuilder.SetPaperOffset(offset, unit));

    // --- Control ---
    public void Clear() => Send(TsplCommandBuilder.Clear());
    public void Cut() => Send(TsplCommandBuilder.Cut());
    public void EndOfJob() => Send(TsplCommandBuilder.EndOfJob());
    public void SelfTest() => Send(TsplCommandBuilder.SelfTest());
    public void InitialPrinter() => Send(TsplCommandBuilder.InitialPrinter());
    public void AutoDetect() => Send(TsplCommandBuilder.AutoDetect());
    public void GapDetect() => Send(TsplCommandBuilder.GapDetect());
    public void BlineDetect() => Send(TsplCommandBuilder.BlineDetect());

    // --- Drawing / print ---
    public void DrawLine(float x, float y, float width, float height) =>
        Send(TsplCommandBuilder.PrintLine(x, y, width, height));
    public void DrawRectangle(float x, float y, float xEnd, float yEnd, float thickness) =>
        Send(TsplCommandBuilder.PrintRectangle(x, y, xEnd, yEnd, thickness));
    public void DrawQRCode(float x, float y, string data,
        char eccLevel = 'M', int cellWidth = 4, char mode = 'A', int rotation = 0) =>
        Send(TsplCommandBuilder.PrintQRCode(x, y, eccLevel, cellWidth, mode, rotation, data));

    /// <summary>Adds a TSPL <c>BITMAP</c> of the image at (x,y) to the current label (no print).</summary>
    public void DrawImage(Bitmap image, float x = 0, float y = 0) =>
        Send(TsplCommandBuilder.PrintBitmap(x, y, MonochromeRasterizer.Rasterize(image)));

    /// <summary><c>PRINT sets,copies</c> — prints the composed label.</summary>
    public void Print(uint sets = 1, uint copies = 1) =>
        Send(TsplCommandBuilder.StartPrint(sets, copies));

    /// <summary>Convenience: clear, draw the image at (x,y), and print one copy (TSPL path).</summary>
    public void PrintImage(Bitmap image, float x = 0, float y = 0)
    {
        Clear();
        DrawImage(image, x, y);
        Print();
    }

    /// <summary>Prints an image as a self-contained CPCL label.</summary>
    public void PrintImageCpcl(Bitmap image, float x = 0, float y = 0) =>
        Send(CpclCommandBuilder.PrintLabel(MonochromeRasterizer.Rasterize(image), x, y));

    // --- Flash store / recall (DOWNLOAD / PUTBMP) ---
    /// <summary>Stores <paramref name="data"/> in printer flash under <paramref name="name"/>
    /// (<c>DOWNLOAD</c>). Recall it with <see cref="PrintStoredBitmap"/>.</summary>
    public void DownloadFile(string name, ReadOnlySpan<byte> data) =>
        Send(TsplCommandBuilder.DownloadFile(name, data));

    /// <summary>Rasterizes <paramref name="image"/> and stores it in flash as a 1-bpp blob under
    /// <paramref name="name"/> (ceil(w/8) stride, TSPL bit convention to match printing).</summary>
    public void DownloadImage(string name, Bitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);
        MonochromeRaster raster = MonochromeRasterizer.Rasterize(image, MonochromeRasterizer.StrideBytes(image.Width));
        var blob = new byte[raster.Data.Length];
        for (int i = 0; i < blob.Length; i++) blob[i] = (byte)(raster.Data[i] ^ 0xFF);
        DownloadFile(name, blob);
    }

    /// <summary>Recalls and prints a flash-stored bitmap at (x,y) (<c>PUTBMP</c>).</summary>
    public void PrintStoredBitmap(string name, float x = 0, float y = 0) =>
        Send(TsplCommandBuilder.PrintDownloadedBitmap(x, y, name));

    /// <summary>
    /// Prints a full TSPL label exactly the way the official CT320B driver does (verified against a
    /// captured print job): the SET-config preamble, double CLS, a <c>BITMAP</c> of the rendered
    /// image (ceil(w/8) stride, TSPL bit convention), then PRINT. This is the known-good path for
    /// real labels on this firmware.
    /// </summary>
    public void PrintImageLabel(
        Bitmap image, float widthMm, float heightMm, float x = 0, float y = 0,
        float gapMm = 2f, int speed = 5, int density = 8, uint copies = 1)
    {
        ArgumentNullException.ThrowIfNull(image);
        _logger.LogInformation("Printing {Width}x{Height} mm label ({W}x{H}px, {Copies} copies).",
            widthMm, heightMm, image.Width, image.Height, copies);
        Send(TsplCommandBuilder.BuildLabelPreamble(widthMm, heightMm, gapMm, speed, density));
        Send(TsplCommandBuilder.Clear());
        Send(TsplCommandBuilder.Clear());
        // ceil(w/8) stride (not DWORD-aligned) to match the driver's BITMAP width field.
        var raster = MonochromeRasterizer.Rasterize(image, MonochromeRasterizer.StrideBytes(image.Width));
        Send(TsplCommandBuilder.PrintBitmap(x, y, raster));
        Send(TsplCommandBuilder.StartPrint(1, copies));
    }

    // --- Status / RFID ---
    /// <summary>
    /// Requests the RFID data field and returns the 48 bytes, or null on timeout / invalid /
    /// CRC-failed reply. Uses a rolling sequence number like the firmware.
    /// </summary>
    public byte[]? ReadRfidData(int timeoutMs = IPrinterTransport.DefaultTimeoutMs)
    {
        Send(StatusCodec.GetRfidDataRequest(_rfidSeq++));
        var buffer = new byte[2048];
        int read = _transport.Read(buffer, timeoutMs);
        if (read <= 0) return null;
        return StatusCodec.TryParseRfidResponse(buffer.AsSpan(0, read), out byte[] data) ? data : null;
    }

    /// <summary>
    /// Requests the Chiteng print mode (command 0x0105) and returns its byte, or null on
    /// timeout / invalid / CRC-failed reply.
    /// </summary>
    public byte? ReadPrintMode(int timeoutMs = IPrinterTransport.DefaultTimeoutMs)
    {
        Send(StatusCodec.GetPrintModeRequest(_rfidSeq++));
        var buffer = new byte[2048];
        int read = _transport.Read(buffer, timeoutMs);
        if (read <= 0) return null;
        return StatusCodec.TryParseReply(buffer.AsSpan(0, read), out StatusCodec.StatusReply reply)
               && reply.Data.Length >= 1
            ? reply.Data[0]
            : null;
    }

    /// <summary>
    /// Requests the Chiteng print memory (slot 53, command 0x0507) and returns its byte, or null
    /// on timeout / invalid / CRC-failed reply (reply must be type 5, subtype 0x87).
    /// </summary>
    public byte? ReadPrintMemory(int timeoutMs = IPrinterTransport.DefaultTimeoutMs)
    {
        Send(StatusCodec.GetPrintMemoryRequest(_rfidSeq++));
        var buffer = new byte[2048];
        int read = _transport.Read(buffer, timeoutMs);
        if (read <= 0) return null;
        return StatusCodec.TryParsePrintMemory(buffer.AsSpan(0, read), out byte value) ? value : null;
    }

    // --- Raw ---
    /// <summary>Sends arbitrary bytes (escape hatch for commands not yet wrapped).</summary>
    public void SendRaw(ReadOnlySpan<byte> data) => Send(data);

    private void Send(ReadOnlySpan<byte> command)
    {
        if (_transport.Write(command) < 0)
        {
            _logger.LogError("Failed to send {Count} bytes to the printer.", command.Length);
            throw new IOException("Failed to send command to the printer.");
        }
        _logger.LogTrace("Sent {Count} bytes.", command.Length);
    }

    public void Dispose()
    {
        _hotPlug?.Dispose();
        _hotPlug = null;
        if (_ownsTransport) _transport.Dispose();
    }
}
