using System.Runtime.InteropServices;
using CT320B.UsbApi.Native;

namespace CT320B.UsbApi.Transport;

/// <summary>
/// Bluetooth RFCOMM transport — a faithful port of <c>CBlueTooth</c>'s socket path. Opens an
/// <c>AF_BTH/SOCK_STREAM/BTHPROTO_RFCOMM</c> socket with authenticate+encrypt socket options and
/// connects to the printer's 48-bit address via a <c>SOCKADDR_BTH</c> whose <c>serviceClassId</c>
/// is the RFCOMM UUID (port 0 ⇒ SDP resolves the channel). Sends/receives the same TSPL/CPCL byte
/// stream as the USB transport, so the whole protocol layer works unchanged.
/// </summary>
public sealed unsafe class RfcommTransport : IPrinterTransport
{
    /// <summary>The serviceClassId the DLL uses: <c>{00000003-0000-1000-8000-00805F9B34FB}</c>
    /// (Bluetooth base UUID + RFCOMM 0x0003).</summary>
    public static readonly Guid RfcommServiceClassId = new("00000003-0000-1000-8000-00805F9B34FB");

    private readonly ulong _deviceAddress;
    private readonly Guid _serviceClassId;
    private IntPtr _socket = WinsockBth.INVALID_SOCKET;

    /// <param name="deviceAddress">48-bit Bluetooth device address (in the low 6 bytes).</param>
    /// <param name="serviceClassId">Service UUID; defaults to <see cref="RfcommServiceClassId"/>.</param>
    public RfcommTransport(ulong deviceAddress, Guid? serviceClassId = null)
    {
        _deviceAddress = deviceAddress;
        _serviceClassId = serviceClassId ?? RfcommServiceClassId;
    }

    /// <summary>Builds a transport from a "AA:BB:CC:DD:EE:FF" address string.</summary>
    public static RfcommTransport FromAddressString(string address, Guid? serviceClassId = null)
        => new(ParseAddress(address), serviceClassId);

    public bool IsOpen => _socket != WinsockBth.INVALID_SOCKET;

    /// <summary>Opens the RFCOMM socket and connects to the printer.</summary>
    public void Connect()
    {
        if (IsOpen) return;
        WinsockBth.EnsureStarted();

        IntPtr s = WinsockBth.socket(WinsockBth.AF_BTH, WinsockBth.SOCK_STREAM, WinsockBth.BTHPROTO_RFCOMM);
        if (s == WinsockBth.INVALID_SOCKET)
            throw new IOException($"socket(AF_BTH) failed (WSA error {WinsockBth.WSAGetLastError()}).");

        int enable = 1;
        WinsockBth.setsockopt(s, WinsockBth.SOL_RFCOMM, WinsockBth.SO_BTH_AUTHENTICATE, in enable, sizeof(int));
        WinsockBth.setsockopt(s, WinsockBth.SOL_RFCOMM, WinsockBth.SO_BTH_ENCRYPT, in enable, sizeof(int));

        var sa = new WinsockBth.SOCKADDR_BTH
        {
            addressFamily = WinsockBth.AF_BTH,
            btAddr = _deviceAddress,
            serviceClassId = _serviceClassId,
            port = 0,
        };

        if (WinsockBth.connect(s, in sa, Marshal.SizeOf<WinsockBth.SOCKADDR_BTH>()) == WinsockBth.SOCKET_ERROR)
        {
            int err = WinsockBth.WSAGetLastError();
            WinsockBth.closesocket(s);
            throw new IOException($"connect to {FormatAddress(_deviceAddress)} failed (WSA error {err}).");
        }

        _socket = s;
    }

    public int Write(ReadOnlySpan<byte> data, int timeoutMs = IPrinterTransport.DefaultTimeoutMs)
    {
        if (!IsOpen || data.IsEmpty) return -1;
        fixed (byte* p = data)
        {
            int sent = WinsockBth.send(_socket, p, data.Length, 0);
            return sent == WinsockBth.SOCKET_ERROR ? -1 : 0;
        }
    }

    public int Read(Span<byte> buffer, int timeoutMs = IPrinterTransport.DefaultTimeoutMs)
    {
        if (!IsOpen || buffer.IsEmpty) return -1;
        // Honour the timeout via SO_RCVTIMEO (the DLL blocks; this is a usability improvement).
        WinsockBth.setsockopt(_socket, WinsockBth.SOL_SOCKET, WinsockBth.SO_RCVTIMEO, in timeoutMs, sizeof(int));
        fixed (byte* p = buffer)
        {
            int read = WinsockBth.recv(_socket, p, buffer.Length, 0);
            return read <= 0 ? -1 : read;
        }
    }

    public void Close()
    {
        if (IsOpen)
        {
            WinsockBth.closesocket(_socket);
            _socket = WinsockBth.INVALID_SOCKET;
        }
    }

    public void Dispose() => Close();

    /// <summary>Parses "AA:BB:CC:DD:EE:FF" (or bare hex) into a 48-bit BTH_ADDR.</summary>
    public static ulong ParseAddress(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        string hex = address.Replace(":", "").Replace("-", "").Trim();
        if (hex.Length != 12 || !ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong value))
            throw new FormatException($"Invalid Bluetooth address: '{address}'.");
        return value;
    }

    /// <summary>Formats a 48-bit BTH_ADDR as "AA:BB:CC:DD:EE:FF".</summary>
    public static string FormatAddress(ulong address)
    {
        Span<char> buf = stackalloc char[17];
        int pos = 0;
        for (int shift = 40; shift >= 0; shift -= 8)
        {
            byte b = (byte)(address >> shift);
            buf[pos++] = ToHex(b >> 4);
            buf[pos++] = ToHex(b & 0xF);
            if (shift > 0) buf[pos++] = ':';
        }
        return new string(buf);

        static char ToHex(int n) => (char)(n < 10 ? '0' + n : 'A' + (n - 10));
    }
}
