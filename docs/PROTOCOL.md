# CT320B Printer Protocol

A practical reference for talking to the **CT320B** thermal label printer, reverse-engineered from its
`USBApi.dll` and ground-truth captures of the official software, and **validated on real hardware**. This
is the distilled spec; the raw asm-level notes (vtable maps, addresses, CRC derivation) live in
[`protocol_internal.md`](protocol_internal.md) and [`bluetooth_internal.md`](bluetooth_internal.md), and the
reference implementation is `src/CT320B.UsbApi/`.

## Overview

- The CT320B is a **TSPL** (TSC-compatible) direct-thermal printer — *despite* some of the vendor app's
  `.ddl` templates being labelled CPCL.
- **203 dpi = 8 dots/mm.** A 30 × 40 mm label is 240 × 320 dots.
- USB identity: **`VID_28E9 & PID_0284`**, driver service `usbprint` ("USB Printing Support"),
  manufacturer `CHITENG`.
- Two channels share the connection:
  - a **text command stream** — ASCII TSPL commands, each terminated by `\r\n`, with floats formatted in
    the invariant culture (`30.00`, decimal point); this is what prints labels.
  - a **binary status side-channel** — `0xDD`-framed, CRC-8 packets for RFID/printer-status queries.

## Transport

| | USB | Bluetooth |
|---|-----|-----------|
| Open | `CreateFile(devicePath, GENERIC_READ\|WRITE, SHARE_READ\|WRITE, OPEN_EXISTING, FILE_FLAG_OVERLAPPED)` | RFCOMM socket (`AF_BTH`) to a **paired** device |
| Enumerate / discover | SetupAPI over `GUID_DEVINTERFACE_USBPRINT` `{28d78fad-5a12-11d1-ae5b-0000f803a8c2}`, parse `USB\VID_xxxx&PID_xxxx\` | `BluetoothFindFirstDevice/Radio`; the printer's name starts `CT` |
| I/O | overlapped `WriteFile`/`ReadFile` (2 s write, caller timeout read) | blocking socket send; receive loop |

Commands are written as raw bytes **without** the trailing NUL (the command's `\r\n` is the terminator).
Bluetooth requires a Windows pairing/bond first — otherwise the RFCOMM `connect` fails (WSA 10051).

## Printing a label

The robust, hardware-proven approach (and what the official `DPrintService.exe` does) is to **render the
whole label to a 1-bpp bitmap and send it as one `BITMAP` command** inside a fixed preamble. Mixing
native vector commands (`BOX`/`QRCODE`/…) into a job without the correct `SET …` preamble can make the
unit beep or power-cycle. Render-to-bitmap keeps you on the one known-good sequence, so the on-screen
preview equals the print.

### The canonical job sequence

Captured from the official driver printing a 30 × 40 mm label (replaying these exact bytes prints a
correct label):

```text
SET RIBBON OFF\r\n            ; direct thermal (no ribbon) — REQUIRED or the printer won't mark
SIZE 30 mm,40 mm\r\n
GAP 2 mm,0 mm\r\n
REFERENCE 0,0\r\n
SPEED 5\r\n
DENSITY 8\r\n
SET PEEL OFF\r\n
SET CUTTER OFF\r\n
SET PARTIAL_CUTTER OFF\r\n
SET TEAR ON\r\n
DIRECTION 0,0\r\n
SHIFT 0\r\n
OFFSET 0 mm\r\n
CLS\r\nCLS\r\n                ; sent twice
BITMAP 5,95,29,105,1,<raster bytes>\r\n
PRINT 1,1\r\n
```

- The **`SET RIBBON OFF` + `SET PEEL/CUTTER/PARTIAL_CUTTER/TEAR` preamble is mandatory.**
- `BITMAP` here is `BITMAP x,y,widthBytes,height,mode,` followed immediately by the raw raster and `\r\n`,
  then a separate `PRINT 1,1`. `x`/`y` are in dots.
- `widthBytes = ceil(width / 8)` (here `29 = ceil(232/8)`) — **not** DWORD-aligned.

## Command reference

All commands are `\r\n`-terminated; floats are invariant (decimal point). Ranges/notes are confirmed from
captures.

| Command | Format | Notes |
|---------|--------|-------|
| `SIZE` | `SIZE %.2f,%.2f` or `SIZE %.0f %s,%.0f %s` | label width,height. Unit form when a unit (`mm`) is given. |
| `GAP` | `GAP %.2f,%.2f` or `GAP %.0f %s,%.0f %s` | gap between labels + offset. |
| `BLINE` | `BLINE %.2f,%.2f` or unit form | black-line mark height + offset. |
| `OFFSET` | `OFFSET %.1f` or `OFFSET %.1f %s` | feed offset. |
| `SPEED` | `SPEED %.0f` / `SPEED %.1f` | **1.0–14.0**; half-steps 1.5/2.5/3.5 use one decimal. |
| `DENSITY` | `DENSITY %d` | **0–15** darkness. |
| `DIRECTION` | `DIRECTION %d,%d` | print direction / mirror. |
| `REFERENCE` | `REFERENCE %d,%d` | origin (dots). The driver sends plain `REFERENCE`; the DLL alternatively emits `DIRECTION %d\nREFERENCE %d,%d`. |
| `SHIFT` | `SHIFT %d` | vertical shift. |
| `SET …` | `SET RIBBON OFF`, `SET PEEL OFF`, `SET CUTTER OFF`, `SET PARTIAL_CUTTER OFF`, `SET TEAR ON` | mode preamble (see above). |
| `CLS` | `CLS` | clear image buffer (the driver sends it twice). |
| `BITMAP` | `BITMAP %.0f,%.0f,%u,%u,%d,` + raster | x, y, widthBytes, height, **mode**, then raw 1-bpp data. |
| `PRINT` | `PRINT %u,%u` | sets, copies. |
| `BAR` | `BAR %.0f,%.0f,%.0f,%.0f` | x, y, width, height (a filled bar). |
| `BOX` | `BOX %.0f,%.0f,%.0f,%.0f,%.0f` | x, y, x_end, y_end, line thickness. |
| `QRCODE` | `QRCODE %.0f,%.0f,%c,%d,%c,%d,"%s"` | x, y, ECC (`L/M/Q/H`), cell width, mode (`A`/`M`), rotation, data. |
| `DOWNLOAD` | `DOWNLOAD "%s",%u,` + data | store a file (e.g. a BMP) in printer flash. |
| `PUTBMP` | `PUTBMP %.0f,%.0f,%s` | recall a stored bitmap by name. |
| `SELFTEST` / `INITIALPRINTER` | — | self-test print / reset to defaults. |
| `AUTODETECN` / `GAPDETECT` / `BLINEDETECT` | — | media calibration (feeds 1–2 labels). |
| `CUT` / `EOJ` | — | cut / end-of-job. |

**`BITMAP` mode** is the standard TSPL graphic mode (0 = OVERWRITE, **1 = OR**, 2 = XOR, 3 = AND); the
driver and all in-DLL paths use mode 1.

**Units** are a per-call choice, not a global setting: pass no unit ⇒ metric `%.2f,%.2f`; pass `mm` ⇒
`%.0f mm,%.0f mm`.

## BITMAP raster format

The raster that follows a `BITMAP` header is packed 1 bit per pixel:

- **Bit 0 = black dot, bit 1 = white** (standard TSPL convention). *Note:* the DLL's internal
  `Bmp2Bytes` uses the inverse (1 = dark), so a port that mirrors `DPrintService` must **invert** before
  sending. (The in-DLL `TscPrintBitmap` path keeps 1 = dark and uses a DWORD-aligned stride — a different
  path; the recommended `BITMAP`-from-buffer path is bit-0-black with `ceil(w/8)` stride.)
- **MSB-first** within each byte: pixel `x` is byte `x/8`, bit `7 − (x%8)`.
- **Row order: top-down, left-to-right** (no bottom-up flip).
- Greyscale reduction: `gray = (R + G + B) / 3`; `gray < 128` ⇒ black (bit set).
- Stride (bytes per row) = **`ceil(width / 8)`** for the `BITMAP` command path.

The managed `MonochromeRasterizer` reproduces this byte-for-byte (verified against the real
`TscPrintBitmap` output).

## Status & RFID side-channel

A separate **binary** protocol (distinct from the text stream) handles RFID reads and printer-status
queries. Frames are `0xDD`-delimited and protected by **CRC-8** (polynomial `0x07`, init `0x00`, no
reflection, no final XOR).

**Request** (8–9 bytes):

```text
DD  seq  <payload…>  crc8  DD
```
- `seq` is a rolling counter; `payload` = `type subtype sizeHi sizeLo [data…]` (size is big-endian).
- `crc8` is computed over `seq` + `payload` (the `0xDD` delimiters are excluded).

**Response** (same framing):

```text
DD  seq  type  subtype  sizeHi  sizeLo  <data…>  crc8  DD
```
- `type` 3 = RFID (48-byte data field), 5 = printer status / mode / memory.
- `usDataFieldSize = (sizeHi << 8) | sizeLo`; `crc8` covers `seq … last data byte`.

Confirmed request codes (payload after `seq`):

| Purpose | Payload | Reply |
|---------|---------|-------|
| Read RFID | `03 01 00 00` | type 3, 48-byte data |
| RFID status | `05 01 00 10 <arg>` | — |
| Status (05/03) | `05 03 00 00 00 <arg>` | `<arg>` = `(uint)float` |
| Status (05/02) | `05 02 00 01 <arg>` | single status byte |
| Status (05/07/01) | `05 07 00 01 <arg>` | single status byte |
| Print memory | `05 07 00 00` | type 5, subtype `0x87`, 1 byte |

The frame, CRC, and parse path are confirmed (and implemented in `StatusCodec`); the precise firmware
meaning of each `arg` in the `0x05` status family is Chiteng-proprietary and undocumented, so the codec
exposes them as generic `BuildFrame(seq, payload)` calls.

## See also

- [`usb_api.md`](usb_api.md) / [`bluetooth_api.md`](bluetooth_api.md) — how to use the C# library over each
  transport.
- `src/CT320B.UsbApi/` — the reference C# implementation (`TsplCommandBuilder`, `MonochromeRasterizer`,
  `StatusCodec`, transports).
