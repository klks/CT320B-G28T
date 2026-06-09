using CT320B.UsbApi.Transport;

namespace CT320B.UsbApi.Enumeration;

/// <summary>A discovered Bluetooth device (candidate printer).</summary>
public sealed record BluetoothPrinterInfo(
    ulong Address,
    string Name,
    bool Authenticated,
    bool Connected)
{
    /// <summary>The device address as "AA:BB:CC:DD:EE:FF".</summary>
    public string AddressString => RfcommTransport.FormatAddress(Address);

    public override string ToString() =>
        $"{Name} [{AddressString}]{(Authenticated ? " paired" : "")}{(Connected ? " connected" : "")}";
}
