using System.Globalization;
using System.Text;
using CT320B.UsbApi.Imaging;

namespace CT320B.UsbApi.Protocol.Cpcl;

/// <summary>
/// Builds CPCL command byte streams matching the DLL's CPCL paths (<c>CpclPrintBitmap</c> and
/// <c>CpclPrintPcx</c>). CPCL graphics use the <c>CG</c> command:
/// <c>CG {widthBytes} {height} {x} {y} </c> followed by the raw 1-bpp raster and a CRLF.
/// All format strings are asm-confirmed (see docs/protocol_internal.md §4).
/// </summary>
public static class CpclCommandBuilder
{
    private const string CRLF = "\r\n";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Default CPCL session header the DLL emits: <c>! 0 200 200 210 1</c> (200 dpi,
    /// 210-dot height, 1 label).</summary>
    public const string DefaultSessionHeader = "! 0 200 200 210 1";

    /// <summary>
    /// A bare <c>CG</c> graphics block: <c>CG widthBytes height x y </c> + raster + <c>\r\n</c>
    /// (= <c>CpclPrintBitmap</c>). Use within an existing CPCL session.
    /// </summary>
    public static byte[] Graphics(MonochromeRaster raster, float x = 0, float y = 0)
    {
        ArgumentNullException.ThrowIfNull(raster);
        return Concat(GraphicsHeader(raster, x, y), raster.Data, Bytes(CRLF));
    }

    /// <summary>
    /// A complete self-contained CPCL label (= <c>CpclPrintPcx</c>):
    /// <c>! 0 200 200 210 1\r\nPAGE-WIDTH 500\r\n</c> + CG block + <c>FORM\r\nPRINT\r\n</c>.
    /// </summary>
    public static byte[] PrintLabel(MonochromeRaster raster, float x = 0, float y = 0)
    {
        ArgumentNullException.ThrowIfNull(raster);
        return Concat(
            Bytes($"{DefaultSessionHeader}{CRLF}PAGE-WIDTH 500{CRLF}"),
            GraphicsHeader(raster, x, y),
            raster.Data,
            Bytes($"{CRLF}FORM{CRLF}PRINT{CRLF}"));
    }

    private static byte[] GraphicsHeader(MonochromeRaster raster, float x, float y) =>
        Bytes($"CG {raster.WidthBytes.ToString(Inv)} {raster.Height.ToString(Inv)} " +
              $"{x.ToString("F0", Inv)} {y.ToString("F0", Inv)} ");

    private static byte[] Bytes(string s) => Encoding.Latin1.GetBytes(s);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        int offset = 0;
        foreach (byte[] p in parts) { p.CopyTo(result, offset); offset += p.Length; }
        return result;
    }
}
