using System.Drawing;
using CT320B.LabelDesigner.Core.Codecs;
using CT320B.LabelDesigner.Core.Rendering;
using ZXing.QrCode.Internal;

namespace CT320B.LabelDesigner.Core.Model.Elements;

/// <summary>QR error-correction level (own enum so the serialized model doesn't depend on ZXing).</summary>
public enum QrErrorCorrection { L, M, Q, H }

/// <summary>Shape of the QR data modules (Phase 15; styling is shape-only — the printer is 1-bpp).</summary>
public enum QrModuleStyle { Square, Dots, Rounded }

/// <summary>Shape of the QR finder patterns ("eyes").</summary>
public enum QrEyeStyle { Square, Rounded }

/// <summary>
/// A QR code rendered via ZXing. The square code is centered within the element's bounds; if the
/// data is empty or invalid a placeholder outline is drawn instead of throwing. Plain square modules
/// with square eyes and no logo use ZXing's bitmap directly (byte-identical to before); any styling —
/// dot/rounded modules, rounded eyes, or a centre logo — switches to the matrix-based styled renderer.
/// </summary>
public sealed class QrElement : LabelElement
{
    /// <summary>The data to encode.</summary>
    public string Data { get; set; } = "";

    /// <summary>Error-correction level.</summary>
    public QrErrorCorrection ErrorCorrection { get; set; } = QrErrorCorrection.M;

    /// <summary>Quiet-zone margin in modules (QR spec recommends ≥ 4; small values save space).</summary>
    public int Margin { get; set; } = 1;

    /// <summary>Shape of the data modules.</summary>
    public QrModuleStyle ModuleStyle { get; set; } = QrModuleStyle.Square;

    /// <summary>Shape of the finder-pattern eyes.</summary>
    public QrEyeStyle EyeStyle { get; set; } = QrEyeStyle.Square;

    /// <summary>Optional centre logo image bytes (PNG/JPG/…); when set the QR is drawn with the styled
    /// renderer and the error-correction level is bumped to at least Q so it stays scannable.</summary>
    public byte[]? LogoData { get; set; }

    /// <summary>Centre-logo size as a percentage of the QR side (clamped to a scannable range).</summary>
    public int LogoScalePercent { get; set; } = 20;

    // Decoded-logo cache (not serialized); keyed on the LogoData reference so clones re-decode lazily.
    private byte[]? _logoKey;
    private Image? _logoImage;

    /// <summary>True when styling/logo means the matrix-based renderer is used (vs. the plain ZXing path).</summary>
    private bool IsStyled => ModuleStyle != QrModuleStyle.Square || EyeStyle != QrEyeStyle.Square || LogoData is not null;

    /// <summary>The effective error-correction level used (logo forces ≥ Q for resilience).</summary>
    public QrErrorCorrection EffectiveEcc =>
        LogoData is not null && ErrorCorrection < QrErrorCorrection.Q ? QrErrorCorrection.Q : ErrorCorrection;

    /// <summary>True when the centre logo is likely too large for the error-correction budget (a UI warning).</summary>
    public bool LogoExceedsBudget
    {
        get
        {
            if (LogoData is null) return false;
            double frac = Math.Clamp(LogoScalePercent / 100.0, 0.08, 0.4) * QrStyledRenderer.LogoKnockoutFactor;
            return frac * frac > QrMatrix.EccBudget(EffectiveEcc) * 0.85;
        }
    }

    public override void Render(Graphics g, RenderContext ctx)
    {
        float x = ctx.MmToPx(XMm), y = ctx.MmToPx(YMm);
        float bw = ctx.MmToPx(WidthMm), bh = ctx.MmToPx(HeightMm);
        float side = Math.Min(bw, bh);
        if (side < 1) return;

        if (string.IsNullOrEmpty(Data))
        {
            ElementPlaceholder.Draw(g, new RectangleF(x, y, bw, bh), "QR");
            return;
        }

        try
        {
            using Bitmap qr = IsStyled ? RenderStyled((int)side) : QrCodeRenderer.Render(Data, (int)side, Margin, MapEcc(ErrorCorrection));
            g.DrawImage(qr, x + (bw - side) / 2f, y + (bh - side) / 2f, side, side);
        }
        catch (Exception)
        {
            ElementPlaceholder.Draw(g, new RectangleF(x, y, bw, bh), "QR?");
        }
    }

    public override void ApplyDataBinding(Func<string, string> resolve) =>
        Data = resolve(Data);

    private Bitmap RenderStyled(int sidePx)
    {
        QrMatrix matrix = QrMatrix.Encode(Data, EffectiveEcc);
        return QrStyledRenderer.Render(matrix, sidePx, Margin, ModuleStyle, EyeStyle, ResolveLogo(), LogoScalePercent);
    }

    private Image? ResolveLogo()
    {
        if (LogoData is null) { _logoImage?.Dispose(); _logoImage = null; _logoKey = null; return null; }
        if (!ReferenceEquals(_logoKey, LogoData))
        {
            _logoImage?.Dispose();
            try { _logoImage = Image.FromStream(new MemoryStream(LogoData, writable: false)); }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException) { _logoImage = null; }
            _logoKey = LogoData;
        }
        return _logoImage;
    }

    private static ErrorCorrectionLevel MapEcc(QrErrorCorrection ecc) => QrMatrix.MapEcc(ecc);
}
