using System.Drawing;
using CT320B.LabelDesigner.Core.Codecs;
using CT320B.LabelDesigner.Core.Rendering;
using ZXing;

namespace CT320B.LabelDesigner.Core.Model.Elements;

/// <summary>Supported barcode symbologies (mapped to ZXing formats). <see cref="DataMatrix"/>,
/// <see cref="Pdf417"/> and <see cref="Aztec"/> are 2-D matrix codes (no human-readable caption);
/// the rest are 1-D. <see cref="Gs1_128"/> is a Code 128 carrying a leading GS1 FNC1.</summary>
public enum BarcodeSymbology
{
    Code128, Code39, Code93, Ean13, Ean8, UpcA, UpcE, Itf, Codabar, Msi, Plessey, Gs1_128,
    DataMatrix, Pdf417, Aztec,
}

/// <summary>
/// A 1-D barcode rendered via ZXing. The bars fill the bounds (minus an optional caption strip); when
/// <see cref="ShowText"/> is set the data is drawn centered beneath. Invalid data for the chosen
/// symbology renders a placeholder rather than throwing.
/// </summary>
public sealed class BarcodeElement : LabelElement
{
    /// <summary>The data to encode.</summary>
    public string Data { get; set; } = "";

    /// <summary>The barcode symbology.</summary>
    public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.Code128;

    /// <summary>Whether to draw the human-readable data beneath the bars.</summary>
    public bool ShowText { get; set; } = true;

    public override void Render(Graphics g, RenderContext ctx)
    {
        float x = ctx.MmToPx(XMm), y = ctx.MmToPx(YMm);
        int w = (int)ctx.MmToPx(WidthMm), h = (int)ctx.MmToPx(HeightMm);
        if (w < 1 || h < 1) return;

        var bounds = new RectangleF(x, y, w, h);
        if (string.IsNullOrEmpty(Data))
        {
            ElementPlaceholder.Draw(g, bounds, "BARCODE");
            return;
        }

        // Fixed-length numeric symbologies (EAN/UPC/ITF) reject the wrong digit count; normalize so they
        // render. The human-readable text shows what's actually encoded.
        string encode = NormalizeData(Symbology, Data);
        if (string.IsNullOrEmpty(encode))
        {
            ElementPlaceholder.Draw(g, bounds, "BARCODE?");
            return;
        }

        // 2-D matrix codes have no human-readable line; they use the whole box.
        bool showText = ShowText && !IsMatrix(Symbology);
        float textH = showText ? Math.Min(h * 0.28f, ctx.MmToPx(3.2)) : 0f;
        int barH = Math.Max(1, (int)(h - textH));
        // GS1-128 is a Code 128 with a leading FNC1; the wire payload carries it, the caption doesn't.
        string payload = Symbology == BarcodeSymbology.Gs1_128 ? "ñ" + encode : encode;   // ñ = ZXing FNC1
        try
        {
            using Bitmap bars = BarcodeRenderer.Render(payload, MapFormat(Symbology), w, barH);
            g.DrawImage(bars, x, y, w, barH);
        }
        catch (Exception)
        {
            ElementPlaceholder.Draw(g, bounds, "BARCODE?");
            return;
        }

        if (showText && textH >= 4f)
        {
            using var font = new Font("Segoe UI", textH * 0.8f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.Black);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(encode, font, brush, new RectangleF(x, y + barH, w, textH), fmt);
        }
    }

    public override void ApplyDataBinding(Func<string, string> resolve) =>
        Data = resolve(Data);

    /// <summary>
    /// Adjusts the data to what the symbology can encode. EAN-13/EAN-8/UPC-A are fixed-length numeric
    /// (the check digit is appended by the encoder), so digits are kept and padded-left / truncated to
    /// the check-less length; ITF needs an even digit count. Other symbologies pass through unchanged.
    /// Returns "" when a numeric symbology has no digits to encode.
    /// </summary>
    internal static string NormalizeData(BarcodeSymbology symbology, string data) => symbology switch
    {
        BarcodeSymbology.Ean13 => FixedDigits(data, 12),
        BarcodeSymbology.Ean8 => FixedDigits(data, 7),
        BarcodeSymbology.UpcA => FixedDigits(data, 11),
        // UPC-E: number-system digit + 6 payload digits (the encoder appends the check digit).
        BarcodeSymbology.UpcE => FixedDigits(data, 7),
        BarcodeSymbology.Itf => EvenDigits(data),
        _ => data,
    };

    /// <summary>True for 2-D matrix symbologies (no human-readable caption; use the whole box).</summary>
    private static bool IsMatrix(BarcodeSymbology s) =>
        s is BarcodeSymbology.DataMatrix or BarcodeSymbology.Pdf417 or BarcodeSymbology.Aztec;

    private static string DigitsOnly(string s) => new(s.Where(char.IsDigit).ToArray());

    private static string FixedDigits(string s, int length)
    {
        string d = DigitsOnly(s);
        if (d.Length == 0) return "";
        return d.Length >= length ? d[..length] : d.PadLeft(length, '0');
    }

    private static string EvenDigits(string s)
    {
        string d = DigitsOnly(s);
        if (d.Length == 0) return "";
        return d.Length % 2 == 0 ? d : "0" + d;
    }

    private static BarcodeFormat MapFormat(BarcodeSymbology s) => s switch
    {
        BarcodeSymbology.Code39 => BarcodeFormat.CODE_39,
        BarcodeSymbology.Code93 => BarcodeFormat.CODE_93,
        BarcodeSymbology.Ean13 => BarcodeFormat.EAN_13,
        BarcodeSymbology.Ean8 => BarcodeFormat.EAN_8,
        BarcodeSymbology.UpcA => BarcodeFormat.UPC_A,
        BarcodeSymbology.UpcE => BarcodeFormat.UPC_E,
        BarcodeSymbology.Itf => BarcodeFormat.ITF,
        BarcodeSymbology.Codabar => BarcodeFormat.CODABAR,
        BarcodeSymbology.Msi => BarcodeFormat.MSI,
        BarcodeSymbology.Plessey => BarcodeFormat.PLESSEY,
        BarcodeSymbology.DataMatrix => BarcodeFormat.DATA_MATRIX,
        BarcodeSymbology.Pdf417 => BarcodeFormat.PDF_417,
        BarcodeSymbology.Aztec => BarcodeFormat.AZTEC,
        // Code128 + Gs1_128 both map to CODE_128 (GS1 differs only by the FNC1 payload prefix).
        _ => BarcodeFormat.CODE_128,
    };
}
