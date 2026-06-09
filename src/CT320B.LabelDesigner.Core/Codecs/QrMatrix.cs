using CT320B.LabelDesigner.Core.Model.Elements;
using ZXing;
using ZXing.QrCode.Internal;

namespace CT320B.LabelDesigner.Core.Codecs;

/// <summary>
/// The bare QR module matrix (no quiet zone) pulled from ZXing's <see cref="Encoder"/>, so we can draw
/// the modules ourselves for styled output (Phase 15) instead of consuming ZXing's finished bitmap.
/// <see cref="Modules"/> is <c>[x, y]</c> with <c>true</c> = a dark module; the three 7×7 finder
/// patterns sit at the top-left, top-right and bottom-left corners (see <see cref="IsFinder"/>).
/// </summary>
public sealed class QrMatrix
{
    /// <summary>Side length in modules (e.g. 21 for version 1).</summary>
    public int Size { get; }

    /// <summary>Dark-module grid, indexed <c>[x, y]</c>.</summary>
    public bool[,] Modules { get; }

    private QrMatrix(int size, bool[,] modules)
    {
        Size = size;
        Modules = modules;
    }

    /// <summary>Encodes <paramref name="content"/> to a module matrix at the given error-correction level.</summary>
    public static QrMatrix Encode(string content, QrErrorCorrection ecc)
    {
        ArgumentException.ThrowIfNullOrEmpty(content);
        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.ERROR_CORRECTION] = MapEcc(ecc) };
        QRCode code = Encoder.encode(content, MapEcc(ecc), hints);
        ByteMatrix m = code.Matrix ?? throw new InvalidOperationException("QR encode produced no matrix.");
        int size = m.Width;
        var mods = new bool[size, size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                mods[x, y] = m[x, y] == 1;
        return new QrMatrix(size, mods);
    }

    /// <summary>The top-left module coordinate of each 7×7 finder pattern (eye).</summary>
    public IEnumerable<(int X, int Y)> FinderOrigins =>
        [(0, 0), (Size - 7, 0), (0, Size - 7)];

    /// <summary>True when module <c>(x, y)</c> falls inside one of the three 7×7 finder patterns.</summary>
    public bool IsFinder(int x, int y)
    {
        foreach ((int ox, int oy) in FinderOrigins)
            if (x >= ox && x < ox + 7 && y >= oy && y < oy + 7) return true;
        return false;
    }

    /// <summary>Maps our error-correction enum to ZXing's level.</summary>
    public static ErrorCorrectionLevel MapEcc(QrErrorCorrection ecc) => ecc switch
    {
        QrErrorCorrection.L => ErrorCorrectionLevel.L,
        QrErrorCorrection.Q => ErrorCorrectionLevel.Q,
        QrErrorCorrection.H => ErrorCorrectionLevel.H,
        _ => ErrorCorrectionLevel.M,
    };

    /// <summary>The fraction (0–1) of the symbol area the error-correction level can recover — a rough
    /// budget used to warn when a centre logo is too large to stay scannable.</summary>
    public static double EccBudget(QrErrorCorrection ecc) => ecc switch
    {
        QrErrorCorrection.L => 0.07,
        QrErrorCorrection.Q => 0.25,
        QrErrorCorrection.H => 0.30,
        _ => 0.15,
    };
}
