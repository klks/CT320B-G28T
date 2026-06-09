namespace CT320B.UsbApi.Enumeration;

/// <summary>
/// A discovered USB-printer device interface (one entry of <c>SearchUSBDevice</c>'s result).
/// <see cref="DevicePath"/> is what <c>UsbPrintTransport</c> opens with CreateFile.
/// </summary>
public sealed record UsbPrinterInfo(
    string DevicePath,
    string Description,
    string InstanceId,
    ushort VendorId,
    ushort ProductId)
{
    /// <summary>Driver service (<c>SPDRP_SERVICE</c>) — the DLL filters on <c>"usbprint"</c>.</summary>
    public string? Service { get; init; }

    /// <summary>Manufacturer (<c>SPDRP_MFG</c>) — the DLL filters on <c>"CHITENG"</c>.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Friendly name (<c>SPDRP_FRIENDLYNAME</c>), if the device exposes one.</summary>
    public string? FriendlyName { get; init; }

    /// <summary>Port string formatted <c>USB%.3d</c> from the interface's <c>"Port Number"</c>
    /// value (= <c>GetUSBPortParam</c>), or null when unavailable.</summary>
    public string? Port { get; init; }

    /// <summary>The interface's <c>"Port Description"</c> value (REG_SZ), or null.</summary>
    public string? PortDescription { get; init; }

    public override string ToString() =>
        $"{Description} [VID_{VendorId:X4}&PID_{ProductId:X4}]{(Port is null ? "" : $" {Port}")} {DevicePath}";
}
