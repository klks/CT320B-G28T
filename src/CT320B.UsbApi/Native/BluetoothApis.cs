using System.Runtime.InteropServices;

namespace CT320B.UsbApi.Native;

/// <summary>
/// P/Invoke surface for the Microsoft Bluetooth device-enumeration APIs
/// (<c>BluetoothFindFirstDevice</c>/<c>…NextDevice</c>/<c>…DeviceClose</c>), used to discover the
/// printer and its 48-bit address — the same family the DLL uses (<c>CBlueTooth::ScanNearbyBthDev</c>).
/// </summary>
internal static class BluetoothApis
{
    public const int BLUETOOTH_MAX_NAME_SIZE = 248;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public int dwSize;
        public int fReturnAuthenticated;
        public int fReturnRemembered;
        public int fReturnUnknown;
        public int fReturnConnected;
        public int fIssueInquiry;
        public byte cTimeoutMultiplier;   // units of 1.28 s, max 48
        public IntPtr hRadio;             // NULL = all radios
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BLUETOOTH_DEVICE_INFO
    {
        public int dwSize;
        public ulong Address;             // BLUETOOTH_ADDRESS union (48-bit in low bytes)
        public uint ulClassofDevice;
        public int fConnected;
        public int fRemembered;
        public int fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BLUETOOTH_MAX_NAME_SIZE)]
        public string szName;
    }

    [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr BluetoothFindFirstDevice(
        in BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl", SetLastError = true)]
    public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    // AUTHENTICATION_REQUIREMENTS
    public const int MITMProtectionNotRequired = 0;

    [DllImport("bthprops.cpl", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint BluetoothAuthenticateDeviceEx(
        IntPtr hwndParentIn, IntPtr hRadioIn, ref BLUETOOTH_DEVICE_INFO pbtdiInout,
        IntPtr pbtOobData, int authenticationRequirement);
}
