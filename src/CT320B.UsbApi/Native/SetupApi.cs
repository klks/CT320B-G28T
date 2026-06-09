using System.Runtime.InteropServices;

namespace CT320B.UsbApi.Native;

/// <summary>
/// P/Invoke surface for SetupAPI device enumeration, mirroring <c>USBDevice::GetUsbDeviceInfo</c>:
/// <c>SetupDiGetClassDevs(GUID_DEVINTERFACE_USBPRINT, …, DIGCF_PRESENT|DIGCF_DEVICEINTERFACE)</c>
/// then enumerate interfaces → interface detail (DevicePath) → device registry props + instance id.
/// Uses the Unicode (W) entry points; device paths/ids are ASCII so this is behaviour-equivalent
/// to the DLL's A calls.
/// </summary>
internal static class SetupApi
{
    /// <summary>GUID_DEVINTERFACE_USBPRINT — the usbprint device-interface class.</summary>
    public static readonly Guid GUID_DEVINTERFACE_USBPRINT =
        new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");

    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    // SetupDiGetDeviceRegistryProperty "Property" selectors.
    public const uint SPDRP_DEVICEDESC = 0x00000000;
    public const uint SPDRP_SERVICE = 0x00000004;
    public const uint SPDRP_MFG = 0x0000000B;
    public const uint SPDRP_FRIENDLYNAME = 0x0000000C;

    public const int ERROR_INSUFFICIENT_BUFFER = 122;  // 0x7A
    public const int ERROR_NO_MORE_ITEMS = 259;        // 0x103

    public const uint KEY_READ = 0x20019;              // the DLL's samDesired for the port key
    public const uint REG_DWORD = 4;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiGetClassDevsW")]
    public static extern IntPtr SetupDiGetClassDevs(
        in Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet, IntPtr DeviceInfoData, in Guid InterfaceClassGuid,
        uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize,
        out uint RequiredSize, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiGetDeviceInstanceIdW")]
    public static extern bool SetupDiGetDeviceInstanceId(
        IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
        char[]? DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiGetDeviceRegistryPropertyW")]
    public static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property,
        out uint PropertyRegDataType, byte[]? PropertyBuffer, uint PropertyBufferSize,
        out uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    /// <summary>Opens the device interface's registry key (…\DeviceClasses\{GUID}\&lt;iface&gt;\#\
    /// Device Parameters) — where usbprint stores "Port Number"/"Port Description", the same values
    /// <c>GetUSBPortParam</c> reads. Returns INVALID_HANDLE_VALUE on failure.</summary>
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiOpenDeviceInterfaceRegKey")]
    public static extern IntPtr SetupDiOpenDeviceInterfaceRegKey(
        IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        uint Reserved, uint samDesired);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode,
        EntryPoint = "RegQueryValueExW")]
    public static extern int RegQueryValueEx(
        IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType,
        byte[]? lpData, ref uint lpcbData);

    [DllImport("advapi32.dll")]
    public static extern int RegCloseKey(IntPtr hKey);
}
