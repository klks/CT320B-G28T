# Using the CT320B library over Bluetooth

How to drive the printer over **Bluetooth (RFCOMM)** with `CT320B.UsbApi`. Once connected, the printing API
is **identical to USB** — the whole protocol layer is transport-agnostic — so this guide focuses on
discovery, pairing, and connecting; for the printing/command calls see [`usb_api.md`](usb_api.md), and for
the wire format see [`PROTOCOL.md`](PROTOCOL.md).

> **Pair first.** The printer must be bonded in **Windows Bluetooth settings** before you connect — the
> RFCOMM socket requires an existing pairing. An unpaired address fails `connect` (WSA error 10051).

```csharp
using CT320B.UsbApi;                 // CT320BPrinter
using CT320B.UsbApi.Enumeration;     // BluetoothDiscovery, BluetoothPrinterInfo
using CT320B.UsbApi.Bluetooth;       // BluetoothPrinterClient (async)
using CT320B.UsbApi.Transport;       // RfcommTransport (address helpers)
```

## Quick print (synchronous)

```csharp
using var image = RenderYourLabel();
using var printer = CT320BPrinter.OpenFirstBluetooth();      // first device named "CT…"
printer.PrintImageLabel(image, widthMm: 30f, heightMm: 40f, x: 8f, y: 8f);
```

After `OpenFirstBluetooth`, `printer` is a normal `CT320BPrinter` — use every method from
[`usb_api.md`](usb_api.md) (`PrintImageLabel`, `DrawQRCode`, `ReadRfidData`, …) unchanged.

## Discovering printers

```csharp
foreach (BluetoothPrinterInfo d in BluetoothDiscovery.Discover())
    Console.WriteLine(d);   // "CT320B-G28T [32:51:24:27:87:99] paired"

// CT-named printers only:
IReadOnlyList<BluetoothPrinterInfo> printers = BluetoothDiscovery.FindPrinters();
```

`Discover(issueInquiry = true, timeoutSeconds = 10, filter = null)`:
- `issueInquiry: true` runs a live radio inquiry (slower, finds new devices); `false` returns only
  remembered/paired/connected devices (fast).
- `BluetoothPrinterInfo` carries `Address` (48-bit `ulong`), `Name`, `Authenticated`, `Connected`, and a
  formatted `AddressString` (`"AA:BB:CC:DD:EE:FF"`). `BluetoothDiscovery.PrinterNamePrefix` is `"CT"`.

## Pairing

Pair via Windows settings (reliable). Best-effort programmatic pairing is available for "Just Works"
devices:

```csharp
BluetoothPrinterInfo dev = BluetoothDiscovery.Discover().First();
bool ok = BluetoothDiscovery.TryPair(dev);   // true if already paired or pairing succeeded
```

`TryPair` may fail or prompt for devices needing a PIN/numeric confirmation — fall back to Windows
settings in that case.

## Connecting (synchronous)

```csharp
// First "CT…" device (optionally auto-pair an unpaired match):
using var a = CT320BPrinter.OpenFirstBluetooth(autoPair: true);

// By address (ulong or string):
using var b = CT320BPrinter.OpenBluetooth(0x325124278799UL);
using var c = CT320BPrinter.OpenBluetooth("32:51:24:27:87:99");

// By name substring, or by index of a scan:
using var d = CT320BPrinter.OpenBluetoothByName("CT320B");
using var e = CT320BPrinter.OpenBluetoothByIndex(0);
```

`OpenFirstBluetooth(match = null, issueInquiry = true, autoPair = false, logger = null)` filters by a
predicate (default: name starts `CT`). All factories accept an optional `ILogger`. Dispose the printer to
disconnect.

## Async / event-driven client

`BluetoothPrinterClient` runs discovery and connection on the thread pool and surfaces progress through
.NET events — ideal for UIs.

```csharp
using var bt = new BluetoothPrinterClient();
bt.DeviceDiscovered      += d  => Console.WriteLine($"found {d}");
bt.ScanCompleted         += () => Console.WriteLine("scan done");
bt.ConnectionStateChanged += s => Console.WriteLine(s);   // Connecting/Connected/Failed/Disconnected
bt.DataReceived          += bytes => { /* see StartReceiveLoop */ };

await bt.ConnectByNameAsync("CT320B");
bt.Printer!.PrintImageLabel(image, 30f, 40f, x: 8f, y: 8f);
```

Methods:
- `ScanAsync(issueInquiry = true, timeoutSeconds = 10, filter = null, ct)` → returns the device list and
  raises `DeviceDiscovered` per match, then `ScanCompleted`.
- `ConnectAsync(ulong | string, ct)`, `ConnectByNameAsync(name, issueInquiry = true, ct)`,
  `ConnectByIndexAsync(index, issueInquiry = true, ct)`.
- `SendAsync(ReadOnlyMemory<byte>, ct)` — raw send (for anything `Printer` doesn't wrap).
- `Disconnect()` / `Dispose()`; `IsConnected`; `Printer` (the live `CT320BPrinter`, or null).

### Receiving data (full-duplex)

The original DLL is send-only; the managed RFCOMM channel can read too. Start a background loop that raises
`DataReceived` for each chunk:

```csharp
bt.DataReceived += chunk => Console.WriteLine($"{chunk.Length} bytes");
bt.StartReceiveLoop(bufferSize: 2048, readTimeoutMs: 1000);
// …
bt.StopReceiveLoop();
```

(The high-level status reads — `Printer.ReadRfidData()` etc. — do their own request/response, so you don't
need the receive loop for those.)

## Address helpers

```csharp
ulong  addr = RfcommTransport.ParseAddress("32:51:24:27:87:99");  // → 0x325124278799
string text = RfcommTransport.FormatAddress(addr);               // → "32:51:24:27:87:99"
```

`RfcommTransport` connects an `AF_BTH / RFCOMM` socket (authenticate + encrypt) to the device, letting SDP
resolve the channel (`RfcommServiceClassId` = the RFCOMM base UUID). You rarely use it directly — the
`Open*`/`Connect*` helpers create it for you.

## Troubleshooting

- **`connect … failed (WSA error 10051)`** — the device isn't paired; pair it in Windows Bluetooth settings.
- **No devices found** — no Bluetooth radio, or the printer is off/asleep; try `issueInquiry: true`.
- **Pairing prompts / fails** — `TryPair` only handles "Just Works"; pair via Windows settings.

## See also

- [`usb_api.md`](usb_api.md) — the printing/command API (identical once connected) and USB transport.
- [`PROTOCOL.md`](PROTOCOL.md) — TSPL commands, the label sequence, BITMAP raster, status frames.
