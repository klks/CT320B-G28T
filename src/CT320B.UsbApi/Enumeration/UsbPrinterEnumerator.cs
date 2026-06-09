using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CT320B.UsbApi.Native;

namespace CT320B.UsbApi.Enumeration;

/// <summary>
/// Enumerates USB printer device interfaces via SetupAPI — the managed equivalent of
/// <c>USBDeviceService::SearchUSBDevice</c> / <c>USBDevice::GetUsbDeviceInfo</c>. Every result is
/// a usbprint interface (matched by <c>GUID_DEVINTERFACE_USBPRINT</c>); the caller picks the
/// CT320B by VID/PID or description.
/// </summary>
public static partial class UsbPrinterEnumerator
{
    /// <summary>
    /// Returns all present USB printer interfaces with their device path and identity. Mirrors
    /// the DLL: <c>SetupDiGetClassDevs(GUID_DEVINTERFACE_USBPRINT, DIGCF_PRESENT|DEVICEINTERFACE)</c>.
    /// </summary>
    public static IReadOnlyList<UsbPrinterInfo> Enumerate()
    {
        var results = new List<UsbPrinterInfo>();

        IntPtr hDevInfo = SetupApi.SetupDiGetClassDevs(
            SetupApi.GUID_DEVINTERFACE_USBPRINT, IntPtr.Zero, IntPtr.Zero,
            SetupApi.DIGCF_PRESENT | SetupApi.DIGCF_DEVICEINTERFACE);

        if (hDevInfo == SetupApi.INVALID_HANDLE_VALUE || hDevInfo == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetupDiGetClassDevs failed (Win32 error {Marshal.GetLastWin32Error()}).");

        try
        {
            var ifData = new SetupApi.SP_DEVICE_INTERFACE_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SetupApi.SP_DEVICE_INTERFACE_DATA>(),
            };

            for (uint index = 0;
                 SetupApi.SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero,
                     SetupApi.GUID_DEVINTERFACE_USBPRINT, index, ref ifData);
                 index++)
            {
                var devInfo = new SetupApi.SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SetupApi.SP_DEVINFO_DATA>(),
                };

                string? devicePath = GetInterfaceDetail(hDevInfo, ref ifData, ref devInfo);
                if (devicePath is null) continue;

                string instanceId = GetInstanceId(hDevInfo, ref devInfo);
                string description = GetStringProperty(hDevInfo, ref devInfo, SetupApi.SPDRP_DEVICEDESC);
                TryParseVidPid(instanceId.Length > 0 ? instanceId : devicePath, out ushort vid, out ushort pid);

                (string? port, string? portDescription) = GetPortParam(hDevInfo, ref ifData);

                results.Add(new UsbPrinterInfo(devicePath, description, instanceId, vid, pid)
                {
                    Service = NullIfEmpty(GetStringProperty(hDevInfo, ref devInfo, SetupApi.SPDRP_SERVICE)),
                    Manufacturer = NullIfEmpty(GetStringProperty(hDevInfo, ref devInfo, SetupApi.SPDRP_MFG)),
                    FriendlyName = NullIfEmpty(GetStringProperty(hDevInfo, ref devInfo, SetupApi.SPDRP_FRIENDLYNAME)),
                    Port = port,
                    PortDescription = portDescription,
                });
            }
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return results;
    }

    private static string? GetInterfaceDetail(
        IntPtr hDevInfo, ref SetupApi.SP_DEVICE_INTERFACE_DATA ifData,
        ref SetupApi.SP_DEVINFO_DATA devInfo)
    {
        // First call sizes the buffer (expects ERROR_INSUFFICIENT_BUFFER).
        SetupApi.SetupDiGetDeviceInterfaceDetail(
            hDevInfo, ref ifData, IntPtr.Zero, 0, out uint required, ref devInfo);
        if (required == 0) return null;

        IntPtr detail = Marshal.AllocHGlobal((int)required);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize: 8 on 64-bit, 6 on 32-bit.
            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupApi.SetupDiGetDeviceInterfaceDetail(
                    hDevInfo, ref ifData, detail, required, out _, ref devInfo))
                return null;

            // DevicePath (wchar) begins right after the 4-byte cbSize field.
            return Marshal.PtrToStringUni(detail + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
        }
    }

    private static string GetInstanceId(IntPtr hDevInfo, ref SetupApi.SP_DEVINFO_DATA devInfo)
    {
        SetupApi.SetupDiGetDeviceInstanceId(hDevInfo, ref devInfo, null, 0, out uint size);
        if (size == 0) return string.Empty;

        var buf = new char[size];
        if (!SetupApi.SetupDiGetDeviceInstanceId(hDevInfo, ref devInfo, buf, size, out _))
            return string.Empty;

        return new string(buf).TrimEnd('\0');
    }

    private static string GetStringProperty(
        IntPtr hDevInfo, ref SetupApi.SP_DEVINFO_DATA devInfo, uint property)
    {
        SetupApi.SetupDiGetDeviceRegistryProperty(
            hDevInfo, ref devInfo, property, out _, null, 0, out uint size);
        if (size == 0) return string.Empty;

        var buf = new byte[size];
        if (!SetupApi.SetupDiGetDeviceRegistryProperty(
                hDevInfo, ref devInfo, property, out _, buf, size, out _))
            return string.Empty;

        return Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    /// <summary>
    /// Reads the interface's <c>"Port Number"</c> (REG_DWORD → <c>USB%.3d</c>) and
    /// <c>"Port Description"</c> (REG_SZ) — the managed equivalent of <c>GetUSBPortParam</c>
    /// (slot 4). Best-effort: returns (null, null) when the values/key are absent.
    /// </summary>
    private static (string? Port, string? PortDescription) GetPortParam(
        IntPtr hDevInfo, ref SetupApi.SP_DEVICE_INTERFACE_DATA ifData)
    {
        IntPtr hKey = SetupApi.SetupDiOpenDeviceInterfaceRegKey(
            hDevInfo, ref ifData, 0, SetupApi.KEY_READ);
        if (hKey == SetupApi.INVALID_HANDLE_VALUE || hKey == IntPtr.Zero)
            return (null, null);

        try
        {
            string? port = null;
            uint size = 4;
            var dword = new byte[4];
            if (SetupApi.RegQueryValueEx(hKey, "Port Number", IntPtr.Zero, out uint type, dword, ref size) == 0
                && type == SetupApi.REG_DWORD)
            {
                uint portNumber = BitConverter.ToUInt32(dword, 0);
                if (portNumber != 0) port = $"USB{portNumber:D3}";   // "USB%.3d"
            }

            string? portDescription = null;
            uint dsize = 0;
            SetupApi.RegQueryValueEx(hKey, "Port Description", IntPtr.Zero, out _, null, ref dsize);
            if (dsize > 0)
            {
                var buf = new byte[dsize];
                if (SetupApi.RegQueryValueEx(hKey, "Port Description", IntPtr.Zero, out _, buf, ref dsize) == 0)
                    portDescription = NullIfEmpty(Encoding.Unicode.GetString(buf).TrimEnd('\0'));
            }

            return (port, portDescription);
        }
        finally
        {
            SetupApi.RegCloseKey(hKey);
        }
    }

    /// <summary>
    /// Parses VID/PID (hex) from a USB instance id or device path, mirroring the DLL's
    /// <c>"USB\VID_%x&amp;PID_%x\"</c> scan. Case-insensitive (paths use lowercase
    /// <c>vid_</c>/<c>pid_</c>, instance ids uppercase).
    /// </summary>
    public static bool TryParseVidPid(string s, out ushort vendorId, out ushort productId)
    {
        vendorId = 0;
        productId = 0;
        if (string.IsNullOrEmpty(s)) return false;

        Match m = VidPidRegex().Match(s);
        if (!m.Success) return false;

        vendorId = Convert.ToUInt16(m.Groups[1].Value, 16);
        productId = Convert.ToUInt16(m.Groups[2].Value, 16);
        return true;
    }

    [GeneratedRegex(@"VID_([0-9A-Fa-f]{1,4})&PID_([0-9A-Fa-f]{1,4})", RegexOptions.IgnoreCase)]
    private static partial Regex VidPidRegex();
}
