using System.Runtime.InteropServices;
using CT320B.UsbApi.Native;

namespace CT320B.UsbApi.Enumeration;

/// <summary>
/// Discovers Bluetooth devices via <c>BluetoothFindFirstDevice</c> — the managed equivalent of
/// the DLL's <c>scanNearBluetooth</c> / <c>CBlueTooth::ScanNearbyBthDev</c>. CT320B printers
/// advertise with names starting "CT" (the DLL's <c>aCt</c> filter).
/// </summary>
public static class BluetoothDiscovery
{
    /// <summary>Default name prefix the DLL filters printers by.</summary>
    public const string PrinterNamePrefix = "CT";

    /// <summary>
    /// Enumerates Bluetooth devices.
    /// </summary>
    /// <param name="issueInquiry">true performs a live radio inquiry (slow, finds new devices);
    /// false returns only remembered/paired/connected devices (fast).</param>
    /// <param name="timeoutSeconds">Inquiry timeout (rounded to 1.28 s units, max ~61 s).</param>
    /// <param name="filter">Optional predicate to keep only matching devices.</param>
    public static IReadOnlyList<BluetoothPrinterInfo> Discover(
        bool issueInquiry = true, int timeoutSeconds = 10,
        Func<BluetoothPrinterInfo, bool>? filter = null)
    {
        var results = new List<BluetoothPrinterInfo>();

        var search = new BluetoothApis.BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = Marshal.SizeOf<BluetoothApis.BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = 1,
            fReturnRemembered = 1,
            fReturnUnknown = 1,
            fReturnConnected = 1,
            fIssueInquiry = issueInquiry ? 1 : 0,
            cTimeoutMultiplier = (byte)Math.Clamp((int)Math.Ceiling(timeoutSeconds / 1.28), 0, 48),
            hRadio = IntPtr.Zero,
        };

        var info = new BluetoothApis.BLUETOOTH_DEVICE_INFO
        {
            dwSize = Marshal.SizeOf<BluetoothApis.BLUETOOTH_DEVICE_INFO>(),
        };

        IntPtr find = BluetoothApis.BluetoothFindFirstDevice(in search, ref info);
        if (find == IntPtr.Zero)
            return results; // no devices, or no radio present

        try
        {
            do
            {
                var device = new BluetoothPrinterInfo(
                    info.Address,
                    info.szName ?? string.Empty,
                    info.fAuthenticated != 0,
                    info.fConnected != 0);

                if (filter is null || filter(device))
                    results.Add(device);

                info.dwSize = Marshal.SizeOf<BluetoothApis.BLUETOOTH_DEVICE_INFO>();
            }
            while (BluetoothApis.BluetoothFindNextDevice(find, ref info));
        }
        finally
        {
            BluetoothApis.BluetoothFindDeviceClose(find);
        }

        return results;
    }

    /// <summary>Finds CT320B-like printers (name starts with <see cref="PrinterNamePrefix"/>).</summary>
    public static IReadOnlyList<BluetoothPrinterInfo> FindPrinters(
        bool issueInquiry = true, int timeoutSeconds = 10) =>
        Discover(issueInquiry, timeoutSeconds,
            d => d.Name.StartsWith(PrinterNamePrefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Best-effort programmatic pairing of a discovered device via <c>BluetoothAuthenticateDeviceEx</c>
    /// (Just Works / MITM-not-required). Returns true on success. For devices that need a PIN or
    /// numeric confirmation this may fail or prompt; pairing once via Windows settings is the
    /// reliable fallback. No-op (returns true) if already authenticated.
    /// </summary>
    public static bool TryPair(BluetoothPrinterInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.Authenticated) return true;

        // Re-fetch the full device record (search by address) for a valid struct to authenticate.
        var info = new BluetoothApis.BLUETOOTH_DEVICE_INFO
        {
            dwSize = Marshal.SizeOf<BluetoothApis.BLUETOOTH_DEVICE_INFO>(),
            Address = device.Address,
        };
        uint rc = BluetoothApis.BluetoothAuthenticateDeviceEx(
            IntPtr.Zero, IntPtr.Zero, ref info, IntPtr.Zero, BluetoothApis.MITMProtectionNotRequired);
        return rc == 0;   // ERROR_SUCCESS
    }
}
