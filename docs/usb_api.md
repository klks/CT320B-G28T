# Using the CT320B library over USB

How to drive the printer over **USB** with `CT320B.UsbApi`. The USB path talks to the Windows
**usbprint** device interface; nothing but the in-box driver is required. For the Bluetooth equivalent see
[`bluetooth_api.md`](bluetooth_api.md), and for the wire format see [`PROTOCOL.md`](PROTOCOL.md).

```csharp
using CT320B.UsbApi;                 // CT320BPrinter
using CT320B.UsbApi.Enumeration;     // UsbPrinterEnumerator, UsbPrinterInfo
using CT320B.UsbApi.Transport;       // UsbPrintTransport (for manual wiring)
```

## Quick print

```csharp
using var image = RenderYourLabel();                 // any System.Drawing.Bitmap
using var printer = CT320BPrinter.OpenFirstUsb();    // first usbprint interface
printer.PrintImageLabel(image, widthMm: 30f, heightMm: 40f, x: 8f, y: 8f);
```

`PrintImageLabel` renders the bitmap into the firmware's one known-good label sequence (the mandatory
`SET …` preamble, double `CLS`, a 1-bpp `BITMAP`, then `PRINT`) — so what you see is what prints. This is
the recommended path for real labels.

## Enumerating printers

`UsbPrinterEnumerator.Enumerate()` returns every present usbprint interface; pick the CT320B by VID/PID
(`0x28E9` / `0x0284`), description, or manufacturer (`CHITENG`).

```csharp
foreach (UsbPrinterInfo p in UsbPrinterEnumerator.Enumerate())
    Console.WriteLine(p);   // e.g. "USB Printing Support [VID_28E9&PID_0284] USB001 \\?\usb#..."

UsbPrinterInfo? ct = UsbPrinterEnumerator.Enumerate()
    .FirstOrDefault(p => p.VendorId == 0x28E9 && p.ProductId == 0x0284);
```

`UsbPrinterInfo` carries `DevicePath`, `Description`, `InstanceId`, `VendorId`, `ProductId`, and the
best-effort `Service`, `Manufacturer`, `FriendlyName`, `Port` (`USB001`), `PortDescription`. `DevicePath`
is what actually opens the device. (`UsbPrinterEnumerator.TryParseVidPid(s, out vid, out pid)` parses
VID/PID from any instance id or path.)

## Opening a connection

```csharp
// 1) First match (optionally filtered):
using var a = CT320BPrinter.OpenFirstUsb();
using var b = CT320BPrinter.OpenFirstUsb(p => p.VendorId == 0x28E9 && p.ProductId == 0x0284);

// 2) A specific device path (e.g. one you stored from Enumerate):
using var c = CT320BPrinter.OpenUsb(devicePath);

// 3) Manual wiring (full control / custom lifetime):
var transport = new UsbPrintTransport(devicePath);
transport.Open();
using var d = new CT320BPrinter(transport, ownsTransport: true);
```

All open methods accept an optional `Microsoft.Extensions.Logging.ILogger` for diagnostics. `CT320BPrinter`
is `IDisposable`; when it owns the transport (the `Open*` factories), disposing it closes the device.

## Printing

**Recommended — render to bitmap:**

```csharp
printer.PrintImageLabel(
    image, widthMm: 30f, heightMm: 40f,
    x: 8f, y: 8f,            // content offset, in dots (8 dots/mm)
    gapMm: 2f, speed: 5, density: 8, copies: 1);
```

**Other image paths:**

```csharp
printer.PrintImage(image, x, y);       // simple TSPL CLS → BITMAP → PRINT (no SET preamble)
printer.PrintImageCpcl(image, x, y);   // a self-contained CPCL label
```

**Composing with native commands** (advanced — note that mixing raw drawing commands without the full
`SET …` preamble can fault this firmware; prefer the bitmap path):

```csharp
printer.SetPaperSize(30, 40, "mm");
printer.SetGap(2, 0, "mm");
printer.SetSpeed(5);
printer.SetDensity(8);          // 0–15
printer.Clear();
printer.DrawRectangle(10, 10, 230, 310, 3);
printer.DrawQRCode(20, 20, "https://example.com", eccLevel: 'M', cellWidth: 4);
printer.DrawImage(image, x: 5, y: 95);
printer.Print(sets: 1, copies: 1);
```

Page setup: `SetPaperSize`, `SetGap`, `SetBlackLine`, `SetDirection`, `SetReference`, `SetSpeed`,
`SetDensity`, `SetOffset`. Control: `Clear`, `Cut`, `EndOfJob`, `SelfTest`, `InitialPrinter`,
`AutoDetect`, `GapDetect`, `BlineDetect`.

## Flash store & recall

Store a bitmap in printer flash once, then recall it cheaply (`DOWNLOAD` / `PUTBMP`):

```csharp
printer.DownloadImage("LOGO.BMP", logoBitmap);   // rasterizes + stores (1-bpp)
printer.PrintStoredBitmap("LOGO.BMP", x: 5, y: 95);
// or store raw bytes: printer.DownloadFile("DATA.BIN", bytes);
```

## Status & RFID

These use the binary side-channel; each returns `null` on timeout / invalid / CRC-failed reply.

```csharp
byte[]? rfid = printer.ReadRfidData();     // 48-byte RFID data field, or null
byte? mode   = printer.ReadPrintMode();    // Chiteng print-mode byte
byte? mem    = printer.ReadPrintMemory();  // Chiteng print-memory byte
```

## Hot-plug auto-reopen

Survive an unplug/replug without re-opening yourself (mirrors the DLL's device-notification reopen):

```csharp
using var printer = CT320BPrinter.OpenFirstUsb();
printer.EnableUsbHotPlugReopen();   // re-opens the same device when it re-arrives; disposed with the printer
```

## Raw bytes & the transport

```csharp
printer.SendRaw("SELFTEST\r\n"u8);     // escape hatch for commands not yet wrapped
bool open = printer.Transport.IsOpen;  // underlying IPrinterTransport
```

## Errors & lifetime

- A failed transport write throws `IOException`; opening a missing/blocked device throws on `Open`
  (`CreateFile` Win32 error).
- `Write` returns `0`/`-1` and `Read` returns bytes/`-1` at the `IPrinterTransport` level; the facade turns
  write failures into exceptions.
- Always `using` / `Dispose` the printer (and any manually-created transport).

## See also

- [`bluetooth_api.md`](bluetooth_api.md) — the same printing API over Bluetooth/RFCOMM.
- [`PROTOCOL.md`](PROTOCOL.md) — TSPL commands, the label sequence, BITMAP raster, status frames.
