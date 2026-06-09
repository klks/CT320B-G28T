using System.Runtime.InteropServices;

namespace CT320B.UsbApi.Native;

/// <summary>
/// P/Invoke surface for Winsock Bluetooth (RFCOMM) sockets, mirroring the calls
/// <c>CBlueTooth::ConnectBluetooth</c> / <c>SendData</c> make: <c>socket(AF_BTH, SOCK_STREAM,
/// BTHPROTO_RFCOMM)</c>, <c>setsockopt</c> (authenticate + encrypt), <c>connect</c> with a
/// <see cref="SOCKADDR_BTH"/>, then <c>send</c>/<c>recv</c>.
/// </summary>
internal static unsafe class WinsockBth
{
    public const int AF_BTH = 32;            // 0x20
    public const int SOCK_STREAM = 1;
    public const int BTHPROTO_RFCOMM = 3;

    public const int SOL_RFCOMM = 3;
    public const int SO_BTH_AUTHENTICATE = unchecked((int)0x80000001);
    public const int SO_BTH_ENCRYPT = 0x00000002;

    public const int SOL_SOCKET = 0xFFFF;
    public const int SO_RCVTIMEO = 0x1006;

    public static readonly IntPtr INVALID_SOCKET = new(-1);
    public const int SOCKET_ERROR = -1;

    /// <summary>SOCKADDR_BTH — packed (30 bytes): family, 48-bit BTH_ADDR, service GUID, port.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SOCKADDR_BTH
    {
        public ushort addressFamily;
        public ulong btAddr;
        public Guid serviceClassId;
        public uint port;
    }

    [DllImport("ws2_32.dll")]
    public static extern int WSAStartup(ushort wVersionRequested, byte[] lpWSAData);

    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern IntPtr socket(int af, int type, int protocol);

    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int connect(IntPtr s, in SOCKADDR_BTH name, int namelen);

    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int setsockopt(IntPtr s, int level, int optname, in int optval, int optlen);

    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int send(IntPtr s, byte* buf, int len, int flags);

    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int recv(IntPtr s, byte* buf, int len, int flags);

    [DllImport("ws2_32.dll", SetLastError = true)]
    public static extern int closesocket(IntPtr s);

    [DllImport("ws2_32.dll")]
    public static extern int WSAGetLastError();

    private static int _started;

    /// <summary>Ensures WSAStartup(2.2) has been called once for the process.</summary>
    public static void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            var data = new byte[408]; // sizeof(WSADATA)
            int rc = WSAStartup(0x0202, data);
            if (rc != 0)
            {
                _started = 0;
                throw new System.IO.IOException($"WSAStartup failed (error {rc}).");
            }
        }
    }
}
