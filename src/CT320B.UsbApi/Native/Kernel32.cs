using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CT320B.UsbApi.Native;

/// <summary>
/// P/Invoke surface for the overlapped file I/O the USB transport needs, mirroring the calls
/// the original <c>USBTransfer</c> makes (CreateFile / WriteFile / ReadFile with OVERLAPPED +
/// a manual-reset event, ResetEvent, WaitForSingleObject).
/// </summary>
internal static unsafe class Kernel32
{
    // dwDesiredAccess
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;

    // dwShareMode
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;

    // dwCreationDisposition
    public const uint OPEN_EXISTING = 3;

    // dwFlagsAndAttributes
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public const int ERROR_IO_PENDING = 997;        // 0x3E5
    public const uint WAIT_OBJECT_0 = 0x00000000;
    public const uint WAIT_TIMEOUT = 0x00000102;
    public const uint INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    public static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool WriteFile(
        SafeFileHandle hFile, byte* lpBuffer, uint nNumberOfBytesToWrite,
        IntPtr lpNumberOfBytesWritten, NativeOverlapped* lpOverlapped);

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool ReadFile(
        SafeFileHandle hFile, byte* lpBuffer, uint nNumberOfBytesToRead,
        IntPtr lpNumberOfBytesRead, NativeOverlapped* lpOverlapped);

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool GetOverlappedResult(
        SafeFileHandle hFile, NativeOverlapped* lpOverlapped,
        out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool CancelIoEx(SafeFileHandle hFile, NativeOverlapped* lpOverlapped);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateEventW(
        IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool ResetEvent(IntPtr hEvent);

    [DllImport("kernel32", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);
}
