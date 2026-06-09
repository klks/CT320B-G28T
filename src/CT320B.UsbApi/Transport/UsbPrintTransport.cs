using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using CT320B.UsbApi.Native;

namespace CT320B.UsbApi.Transport;

/// <summary>
/// USB transport over the Windows usbprint device, a faithful port of the original
/// <c>USBTransfer</c>: opens the device path with overlapped I/O and the exact CreateFile flags
/// the DLL uses, and performs each Write/Read as an overlapped operation gated by a manual-reset
/// event with a timeout (DLL pattern: ResetEvent → WriteFile → on ERROR_IO_PENDING
/// WaitForSingleObject(timeout) → ResetEvent).
///
/// Device paths come from <c>UsbPrinterEnumerator</c> (SetupAPI, GUID_DEVINTERFACE_USBPRINT).
/// </summary>
public sealed unsafe class UsbPrintTransport : IPrinterTransport
{
    private readonly string _devicePath;
    private SafeFileHandle? _handle;
    private IntPtr _writeEvent;   // USBTransfer write OVERLAPPED.hEvent (orig at +0x28)
    private IntPtr _readEvent;    // USBTransfer read  OVERLAPPED.hEvent (orig at +0x14)

    public UsbPrintTransport(string devicePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(devicePath);
        _devicePath = devicePath;
    }

    /// <summary>The usbprint device interface path this transport targets.</summary>
    public string DevicePath => _devicePath;

    public bool IsOpen => _handle is { IsInvalid: false, IsClosed: false };

    /// <summary>
    /// Open the device. CreateFile flags match the DLL exactly:
    /// access 0xC0000000 (GENERIC_READ|WRITE), share 0x3 (READ|WRITE), OPEN_EXISTING,
    /// flags 0x40000080 (FILE_ATTRIBUTE_NORMAL|FILE_FLAG_OVERLAPPED).
    /// </summary>
    public void Open()
    {
        if (IsOpen) return;

        var handle = Kernel32.CreateFile(
            _devicePath,
            Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            Kernel32.FILE_ATTRIBUTE_NORMAL | Kernel32.FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException($"CreateFile('{_devicePath}') failed (Win32 error {err}).");
        }

        _writeEvent = Kernel32.CreateEventW(IntPtr.Zero, bManualReset: true, bInitialState: false, null);
        _readEvent = Kernel32.CreateEventW(IntPtr.Zero, bManualReset: true, bInitialState: false, null);
        if (_writeEvent == IntPtr.Zero || _readEvent == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            CloseEvents();
            handle.Dispose();
            throw new IOException($"CreateEvent failed (Win32 error {err}).");
        }

        _handle = handle;
    }

    public int Write(ReadOnlySpan<byte> data, int timeoutMs = IPrinterTransport.DefaultTimeoutMs)
    {
        if (!IsOpen || data.IsEmpty) return -1;
        fixed (byte* p = data)
        {
            int n = Overlapped(p, data.Length, _writeEvent, write: true, timeoutMs);
            return n < 0 ? -1 : 0;   // DLL returns 0 on success
        }
    }

    public int Read(Span<byte> buffer, int timeoutMs = IPrinterTransport.DefaultTimeoutMs)
    {
        if (!IsOpen || buffer.IsEmpty) return -1;
        fixed (byte* p = buffer)
            return Overlapped(p, buffer.Length, _readEvent, write: false, timeoutMs);
    }

    /// <summary>
    /// Shared overlapped op. On timeout we CancelIoEx + drain (GetOverlappedResult bWait:true)
    /// so the pending I/O can never touch the caller's buffer after the <c>fixed</c> block ends.
    /// Returns bytes transferred, or -1 on failure/timeout.
    /// </summary>
    private int Overlapped(byte* buf, int len, IntPtr evt, bool write, int timeoutMs)
    {
        SafeFileHandle handle = _handle!;
        Kernel32.ResetEvent(evt);
        var ov = new NativeOverlapped { EventHandle = evt };

        bool ok = write
            ? Kernel32.WriteFile(handle, buf, (uint)len, IntPtr.Zero, &ov)
            : Kernel32.ReadFile(handle, buf, (uint)len, IntPtr.Zero, &ov);

        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != Kernel32.ERROR_IO_PENDING)
            {
                Kernel32.ResetEvent(evt);
                return -1;
            }

            uint wait = Kernel32.WaitForSingleObject(evt, (uint)timeoutMs);
            if (wait != Kernel32.WAIT_OBJECT_0)
            {
                Kernel32.CancelIoEx(handle, &ov);
                Kernel32.GetOverlappedResult(handle, &ov, out _, bWait: true);
                Kernel32.ResetEvent(evt);
                return -1;
            }
        }

        bool gotResult = Kernel32.GetOverlappedResult(handle, &ov, out uint transferred, bWait: false);
        Kernel32.ResetEvent(evt);
        return gotResult ? (int)transferred : -1;
    }

    /// <summary>Closes and re-opens the device — the core of <c>OnReopenTimer</c>'s hot-plug
    /// recovery (e.g. after the printer is re-plugged). Returns true if open afterwards.</summary>
    public bool Reopen()
    {
        Close();
        Open();
        return IsOpen;
    }

    public void Close()
    {
        _handle?.Dispose();
        _handle = null;
        CloseEvents();
    }

    private void CloseEvents()
    {
        if (_writeEvent != IntPtr.Zero) { Kernel32.CloseHandle(_writeEvent); _writeEvent = IntPtr.Zero; }
        if (_readEvent != IntPtr.Zero) { Kernel32.CloseHandle(_readEvent); _readEvent = IntPtr.Zero; }
    }

    public void Dispose() => Close();
}
