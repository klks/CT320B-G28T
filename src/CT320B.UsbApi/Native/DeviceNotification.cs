using System.Runtime.InteropServices;

namespace CT320B.UsbApi.Native;

/// <summary>
/// Whether a device interface arrived or was removed (the <c>WM_DEVICECHANGE</c> events the DLL
/// reacts to: <c>DBT_DEVICEARRIVAL</c> / <c>DBT_DEVICEREMOVECOMPLETE</c>).
/// </summary>
public enum DeviceChange
{
    Arrival,
    Removal,
}

/// <summary>
/// A message-only window that listens for USB device-interface hot-plug events, the managed
/// equivalent of the DLL's hidden <c>"USBDeviceService"</c> window + <c>RegisterDeviceNotification</c>
/// (wndproc <c>sub_10009130</c>, <c>WM_DEVICECHANGE 0x219</c>). Runs its own message pump on a
/// dedicated thread and raises <see cref="DeviceChanged"/> for the registered interface class.
/// </summary>
public sealed class DeviceNotificationWindow : IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int WM_CLOSE = 0x0010;
    private const int WM_DESTROY = 0x0002;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;

    private readonly Guid _interfaceClass;
    private readonly WndProc _wndProc;   // kept alive for the window's lifetime
    private readonly Thread _pumpThread;
    private readonly ManualResetEventSlim _ready = new(false);
    private IntPtr _hwnd;
    private IntPtr _hNotify;
    private volatile bool _disposed;

    /// <summary>Raised (on the pump thread) when a matching device interface arrives or is removed.</summary>
    public event Action<DeviceChange>? DeviceChanged;

    public DeviceNotificationWindow(Guid interfaceClass)
    {
        _interfaceClass = interfaceClass;
        _wndProc = WindowProc;
        _pumpThread = new Thread(Pump) { IsBackground = true, Name = "CT320B-DeviceNotify" };
        _pumpThread.Start();
        _ready.Wait();   // block until the window + registration exist (or failed)
    }

    private void Pump()
    {
        string className = "CT320B_DevNotify_" + Guid.NewGuid().ToString("N");
        var wndClass = new WNDCLASS
        {
            lpfnWndProc = _wndProc,
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };

        try
        {
            if (RegisterClassW(ref wndClass) == 0)
                return;

            // HWND_MESSAGE (-3) → a message-only window: no UI, just receives messages.
            _hwnd = CreateWindowExW(0, className, className, 0, 0, 0, 0, 0,
                new IntPtr(-3), IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                return;

            RegisterForInterface();
        }
        finally
        {
            _ready.Set();
        }

        // Standard pump; exits when the window is destroyed (Dispose posts WM_CLOSE/DestroyWindow).
        while (!_disposed && GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    private void RegisterForInterface()
    {
        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = (uint)Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_classguid = _interfaceClass,
        };
        IntPtr buf = Marshal.AllocHGlobal((int)filter.dbcc_size);
        try
        {
            Marshal.StructureToPtr(filter, buf, false);
            _hNotify = RegisterDeviceNotificationW(_hwnd, buf, DEVICE_NOTIFY_WINDOW_HANDLE);
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_DEVICECHANGE:
                int evt = wParam.ToInt32();
                if (evt is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE && lParam != IntPtr.Zero
                    && Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_DEVICEINTERFACE)   // dbcc_devicetype
                {
                    DeviceChanged?.Invoke(evt == DBT_DEVICEARRIVAL ? DeviceChange.Arrival : DeviceChange.Removal);
                }
                break;

            case WM_DESTROY:
                // Cleanup on the owning thread, then end the pump.
                if (_hNotify != IntPtr.Zero) { UnregisterDeviceNotification(_hNotify); _hNotify = IntPtr.Zero; }
                PostQuitMessage(0);
                break;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // DestroyWindow must run on the owning thread: ask the window to close; DefWindowProc's
        // WM_CLOSE → DestroyWindow → WM_DESTROY handler unregisters and posts WM_QUIT.
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _pumpThread.Join(1000);
        _hwnd = IntPtr.Zero;
        _ready.Dispose();
    }

    // --- P/Invoke ---
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)] public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public uint dbcc_size;
        public uint dbcc_devicetype;
        public uint dbcc_reserved;
        public Guid dbcc_classguid;
        public short dbcc_name;   // first WCHAR of the name (variable-length; unused here)
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
        IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotificationW(IntPtr hRecipient, IntPtr notificationFilter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
