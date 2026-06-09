using CT320B.UsbApi.Enumeration;

namespace CT320B.LabelDesigner.Services;

/// <summary>A selectable printer in the device list — either a USB interface or a Bluetooth device.
/// The unified base lets one list/combo hold both transports.</summary>
public abstract class DeviceTarget
{
    /// <summary>Human-readable label for the device list.</summary>
    public abstract string Display { get; }

    public override string ToString() => Display;
}

/// <summary>A USB printer interface (opened by device path).</summary>
public sealed class UsbDeviceTarget(UsbPrinterInfo info) : DeviceTarget
{
    public UsbPrinterInfo Info { get; } = info;

    public override string Display =>
        $"USB · {Info.FriendlyName ?? Info.Description} [{Info.VendorId:X4}:{Info.ProductId:X4}]"
        + (Info.Port is null ? "" : $" ({Info.Port})");
}

/// <summary>A Bluetooth device (connected by RFCOMM address).</summary>
public sealed class BluetoothDeviceTarget(BluetoothPrinterInfo info) : DeviceTarget
{
    public BluetoothPrinterInfo Info { get; } = info;

    public override string Display =>
        $"BT · {(string.IsNullOrEmpty(Info.Name) ? "(unnamed)" : Info.Name)} [{Info.AddressString}]"
        + (Info.Authenticated ? "" : " — unpaired");
}
