using System.Globalization;
using System.Text;
using CT320B.UsbApi.Imaging;

namespace CT320B.UsbApi.Protocol.Tspl;

/// <summary>
/// Builds TSPL/TSC command byte streams byte-for-byte identical to <c>USBDeviceService</c>'s
/// output (verified against native-oracle captures in <c>tests/.../Fixtures/golden</c>).
///
/// Two emission shapes exist in the DLL:
/// <list type="bullet">
/// <item>Simple commands go through <c>SingleCmd</c>, which appends <c>\r\n</c> — so the command
/// word itself carries no terminator (e.g. <c>SELFTEST\r\n</c>).</item>
/// <item>Formatted commands embed their own terminator in the format string.</item>
/// </list>
/// All numbers are formatted with <see cref="CultureInfo.InvariantCulture"/> (decimal point).
/// </summary>
public static class TsplCommandBuilder
{
    private const string CRLF = "\r\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // --- Simple commands (= SingleCmd: word + CRLF) ---
    public static byte[] SelfTest() => Bytes("SELFTEST" + CRLF);
    public static byte[] InitialPrinter() => Bytes("INITIALPRINTER" + CRLF);
    public static byte[] Cut() => Bytes("CUT" + CRLF);
    public static byte[] EndOfJob() => Bytes("EOJ" + CRLF);
    public static byte[] Clear() => Bytes("CLS" + CRLF);
    public static byte[] AutoDetect() => Bytes("AUTODETECN" + CRLF);
    public static byte[] GapDetect() => Bytes("GAPDETECT" + CRLF);
    public static byte[] BlineDetect() => Bytes("BLINEDETECT" + CRLF);

    // --- Print ---
    /// <summary><c>PRINT sets,copies</c>.</summary>
    public static byte[] StartPrint(uint sets, uint copies) =>
        Bytes($"PRINT {sets},{copies}{CRLF}");

    // --- Setup ---
    /// <summary><c>DENSITY d</c> (0–15).</summary>
    public static byte[] SetPrintDensity(int density)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(density, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(density, 15);
        return Bytes($"DENSITY {density.ToString(Inv)}{CRLF}");
    }

    /// <summary><c>DIRECTION x,y</c>.</summary>
    public static byte[] SetPrintDirection(int x, int y) =>
        Bytes($"DIRECTION {x.ToString(Inv)},{y.ToString(Inv)}{CRLF}");

    /// <summary><c>SIZE</c> — metric <c>%.2f,%.2f</c> when <paramref name="unit"/> is empty,
    /// else <c>%.0f unit,%.0f unit</c>.</summary>
    public static byte[] SetPrintPaperSize(float width, float height, string? unit = null) =>
        Dimension2("SIZE", width, height, unit, "F2");

    /// <summary><c>GAP</c> — same form selection as SIZE.</summary>
    public static byte[] SetPrintPaperGap(float gap, float offset, string? unit = null) =>
        Dimension2("GAP", gap, offset, unit, "F2");

    /// <summary><c>BLINE</c> — same form selection as SIZE.</summary>
    public static byte[] SetBlackLine(float height, float offset, string? unit = null) =>
        Dimension2("BLINE", height, offset, unit, "F2");

    /// <summary><c>OFFSET %.1f</c> (with optional <c>unit</c> suffix).</summary>
    public static byte[] SetPaperOffset(float offset, string? unit = null) =>
        Bytes(string.IsNullOrEmpty(unit)
            ? $"OFFSET {offset.ToString("F1", Inv)}{CRLF}"
            : $"OFFSET {offset.ToString("F1", Inv)} {unit}{CRLF}");

    /// <summary>
    /// <c>SPEED</c> (1.0–14.0). Uses <c>%.0f</c> except for the half-steps 1.5/2.5/3.5 which use
    /// <c>%.1f</c> (matching the DLL's exact-value check).
    /// </summary>
    public static byte[] SetPrintSpeed(float speed)
    {
        if (speed < 1.0f || speed > 14.0f)
            throw new ArgumentOutOfRangeException(nameof(speed), speed, "Speed must be between 1.0 and 14.0.");
        bool half = speed == 1.5f || speed == 2.5f || speed == 3.5f;
        return Bytes($"SPEED {speed.ToString(half ? "F1" : "F0", Inv)}{CRLF}");
    }

    /// <summary>
    /// <c>DIRECTION direction\nREFERENCE refX,refY</c>. Note: the DLL emits a bare <c>\n</c>
    /// separator and <b>no</b> trailing terminator on this command.
    /// </summary>
    public static byte[] SetPrintReference(int refX, int refY, int direction) =>
        Bytes($"DIRECTION {direction.ToString(Inv)}\nREFERENCE {refX.ToString(Inv)},{refY.ToString(Inv)}");

    // --- Drawing ---
    /// <summary><c>BAR x,y,width,height</c> (all <c>%.0f</c>).</summary>
    public static byte[] PrintLine(float x, float y, float width, float height) =>
        Bytes($"BAR {F0(x)},{F0(y)},{F0(width)},{F0(height)}{CRLF}");

    /// <summary><c>BOX x,y,xEnd,yEnd,thickness</c> (all <c>%.0f</c>).</summary>
    public static byte[] PrintRectangle(float x, float y, float xEnd, float yEnd, float thickness) =>
        Bytes($"BOX {F0(x)},{F0(y)},{F0(xEnd)},{F0(yEnd)},{F0(thickness)}{CRLF}");

    /// <summary><c>QRCODE x,y,ecc,cellWidth,mode,rotation,"data"</c>.</summary>
    public static byte[] PrintQRCode(
        float x, float y, char eccLevel, int cellWidth, char mode, int rotation, string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Bytes($"QRCODE {F0(x)},{F0(y)},{eccLevel},{cellWidth.ToString(Inv)},{mode}," +
                     $"{rotation.ToString(Inv)},\"{data}\"{CRLF}");
    }

    // --- Device config (the preamble the real driver sends; see docs/protocol_internal.md §8) ---
    public static byte[] SetRibbon(bool on) => Bytes($"SET RIBBON {OnOff(on)}{CRLF}");
    public static byte[] SetPeel(bool on) => Bytes($"SET PEEL {OnOff(on)}{CRLF}");
    public static byte[] SetCutter(bool on) => Bytes($"SET CUTTER {OnOff(on)}{CRLF}");
    public static byte[] SetPartialCutter(bool on) => Bytes($"SET PARTIAL_CUTTER {OnOff(on)}{CRLF}");
    public static byte[] SetTear(bool on) => Bytes($"SET TEAR {OnOff(on)}{CRLF}");
    public static byte[] Shift(int dots) => Bytes($"SHIFT {dots.ToString(Inv)}{CRLF}");

    /// <summary>Standalone <c>REFERENCE x,y</c> (the plain form the driver sends — distinct from
    /// <see cref="SetPrintReference"/>, which emits a combined DIRECTION+REFERENCE).</summary>
    public static byte[] Reference(int x, int y) =>
        Bytes($"REFERENCE {x.ToString(Inv)},{y.ToString(Inv)}{CRLF}");

    private static string OnOff(bool on) => on ? "ON" : "OFF";

    /// <summary>
    /// The exact device-setup preamble the real CT320B driver (<c>DPrintService</c>) sends before
    /// a label, byte-for-byte (verified against a capture): SET RIBBON OFF, SIZE, GAP, REFERENCE,
    /// SPEED, DENSITY, SET PEEL/CUTTER/PARTIAL_CUTTER/TEAR, DIRECTION, SHIFT, OFFSET. Caller then
    /// sends <see cref="Clear"/> (the driver sends it twice), the BITMAP, and <see cref="StartPrint"/>.
    /// </summary>
    public static byte[] BuildLabelPreamble(
        float widthMm, float heightMm, float gapMm = 2f, int speed = 5, int density = 8)
    {
        var sb = new StringBuilder();
        sb.Append("SET RIBBON OFF").Append(CRLF);
        sb.Append("SIZE ").Append(F0(widthMm)).Append(" mm,").Append(F0(heightMm)).Append(" mm").Append(CRLF);
        sb.Append("GAP ").Append(F0(gapMm)).Append(" mm,0 mm").Append(CRLF);
        sb.Append("REFERENCE 0,0").Append(CRLF);
        sb.Append("SPEED ").Append(speed.ToString(Inv)).Append(CRLF);
        sb.Append("DENSITY ").Append(density.ToString(Inv)).Append(CRLF);
        sb.Append("SET PEEL OFF").Append(CRLF);
        sb.Append("SET CUTTER OFF").Append(CRLF);
        sb.Append("SET PARTIAL_CUTTER OFF").Append(CRLF);
        sb.Append("SET TEAR ON").Append(CRLF);
        sb.Append("DIRECTION 0,0").Append(CRLF);
        sb.Append("SHIFT 0").Append(CRLF);
        sb.Append("OFFSET 0 mm").Append(CRLF);
        return Bytes(sb.ToString());
    }

    // --- Bitmap ---
    /// <summary>
    /// <c>BITMAP x,y,widthBytes,height,mode,</c> + raster + <c>\r\n</c>. The raster is emitted in
    /// **TSPL BITMAP convention** (bit 0 = black dot, 1 = white) — i.e. the rasterizer's bits
    /// (1 = dark) are inverted here. Use a <see cref="MonochromeRasterizer.StrideBytes"/>-strided
    /// raster (ceil(w/8)); the printer's <c>BITMAP</c> width field is bytes-per-row, not
    /// DWORD-aligned. Emits only the BITMAP command; issue <see cref="StartPrint"/> to print.
    /// </summary>
    public static byte[] PrintBitmap(float x, float y, MonochromeRaster raster, int mode = 1)
    {
        ArgumentNullException.ThrowIfNull(raster);
        byte[] header = Bytes(
            $"BITMAP {F0(x)},{F0(y)},{raster.WidthBytes.ToString(Inv)},{raster.Height.ToString(Inv)},{mode.ToString(Inv)},");
        byte[] inverted = new byte[raster.Data.Length];
        for (int i = 0; i < inverted.Length; i++) inverted[i] = (byte)(raster.Data[i] ^ 0xFF);
        return Concat(header, inverted, Bytes(CRLF));
    }

    // --- Flash store / recall ---
    /// <summary>
    /// <c>DOWNLOAD "name",len,</c> + the raw <paramref name="data"/> bytes (no trailing
    /// terminator) — stores a file in the printer's flash (= <c>DownloadBitmap</c>, slot 35,
    /// format <c>DOWNLOAD "%s",%u,</c>). The original DLL strips the path and appends <c>.bin</c>
    /// to derive the on-printer name; pass the final name you want stored (e.g. <c>"LOGO.BMP"</c>).
    /// Recall it later with <see cref="PrintDownloadedBitmap"/>.
    /// </summary>
    public static byte[] DownloadFile(string name, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        byte[] header = Bytes($"DOWNLOAD \"{name}\",{data.Length.ToString(Inv)},");
        var result = new byte[header.Length + data.Length];
        header.CopyTo(result, 0);
        data.CopyTo(result.AsSpan(header.Length));
        return result;
    }

    /// <summary>
    /// <c>PUTBMP x,y,name\r\n</c> — recalls and prints a flash-stored bitmap (= slot 36
    /// <c>PrintDownloadedBitmap</c>, format <c>PUTBMP %.0f,%.0f,%s\r\n</c>). Note: the DLL passes
    /// the name through <c>%s</c> <b>unquoted</b> (unlike DOWNLOAD); include quotes in
    /// <paramref name="name"/> only if your firmware requires them.
    /// </summary>
    public static byte[] PrintDownloadedBitmap(float x, float y, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Bytes($"PUTBMP {F0(x)},{F0(y)},{name}{CRLF}");
    }

    // --- helpers ---
    private static string F0(float v) => v.ToString("F0", Inv);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        int offset = 0;
        foreach (byte[] p in parts) { p.CopyTo(result, offset); offset += p.Length; }
        return result;
    }

    private static byte[] Dimension2(string cmd, float a, float b, string? unit, string metricFmt) =>
        Bytes(string.IsNullOrEmpty(unit)
            ? $"{cmd} {a.ToString(metricFmt, Inv)},{b.ToString(metricFmt, Inv)}{CRLF}"
            : $"{cmd} {F0(a)} {unit},{F0(b)} {unit}{CRLF}");

    /// <summary>Encodes command text as bytes (Latin-1 preserves the DLL's narrow-char bytes 0–255).</summary>
    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);
}
