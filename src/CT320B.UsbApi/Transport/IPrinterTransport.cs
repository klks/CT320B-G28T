namespace CT320B.UsbApi.Transport;

/// <summary>
/// Transport-agnostic byte channel to the printer. The protocol layer (TSPL/CPCL builders,
/// status codec) only sends/receives on an already-open transport, so the same code works over
/// USB (<see cref="UsbPrintTransport"/>) and Bluetooth/RFCOMM. Opening/connecting is
/// transport-specific and lives on the concrete types.
///
/// Return-value convention mirrors the original DLL:
/// <list type="bullet">
/// <item><see cref="Write"/> returns 0 on success, -1 on failure/timeout.</item>
/// <item><see cref="Read"/> returns the number of bytes read, or -1 on failure/timeout.</item>
/// </list>
/// </summary>
public interface IPrinterTransport : IDisposable
{
    /// <summary>Default I/O timeout (ms). Matches the DLL's 0x7D0 (2000 ms) write timeout.</summary>
    public const int DefaultTimeoutMs = 2000;

    /// <summary>True when the channel is open and ready for I/O.</summary>
    bool IsOpen { get; }

    /// <summary>Send raw command bytes. Returns 0 on success, -1 on failure/timeout.</summary>
    int Write(ReadOnlySpan<byte> data, int timeoutMs = DefaultTimeoutMs);

    /// <summary>Receive into <paramref name="buffer"/>. Returns bytes read, or -1 on failure.</summary>
    int Read(Span<byte> buffer, int timeoutMs = DefaultTimeoutMs);

    /// <summary>Close the channel (idempotent).</summary>
    void Close();
}
