namespace CT320B.LabelDesigner.Core.Model;

/// <summary>
/// The single source of truth for unit conversions on the CT320B. Everything in the model is
/// stored in <b>millimetres</b> (resolution-independent); this class is the only place that knows
/// the printer's dot pitch and how millimetres map to device dots and on-screen pixels.
///
/// The printer is a 203-dpi thermal head, conventionally <b>8 dots/mm</b> (203 dpi ≈ 7.992 dots/mm,
/// rounded to the nominal 8 the firmware uses for its coordinate math — see <c>docs/protocol_internal.md §7</c>).
/// At 8 dots/mm a 30×40 mm label is a 240×320-dot bitmap.
/// </summary>
public static class Units
{
    /// <summary>Nominal printer resolution in dots-per-inch.</summary>
    public const double Dpi = 203.0;

    /// <summary>Printer dot pitch: 8 dots per millimetre (the firmware's nominal factor).</summary>
    public const double DotsPerMm = 8.0;

    /// <summary>Millimetres per inch.</summary>
    public const double MmPerInch = 25.4;

    /// <summary>Millimetres → printer dots, rounded to the nearest whole dot (e.g. 30 mm → 240).</summary>
    public static int MmToDots(double mm) => (int)Math.Round(mm * DotsPerMm, MidpointRounding.AwayFromZero);

    /// <summary>Millimetres → printer dots as a real number (sub-dot precision for coordinates).</summary>
    public static double MmToDotsF(double mm) => mm * DotsPerMm;

    /// <summary>Printer dots → millimetres (e.g. 240 dots → 30 mm).</summary>
    public static double DotsToMm(double dots) => dots / DotsPerMm;

    /// <summary>Pixels-per-millimetre at an arbitrary rendering dpi (e.g. a zoomed screen canvas).</summary>
    public static double PixelsPerMmAt(double dpi) => dpi / MmPerInch;

    /// <summary>Millimetres → pixels at a given pixels-per-mm scale.</summary>
    public static double MmToPixels(double mm, double pixelsPerMm) => mm * pixelsPerMm;

    /// <summary>Pixels → millimetres at a given pixels-per-mm scale.</summary>
    public static double PixelsToMm(double pixels, double pixelsPerMm) => pixels / pixelsPerMm;

    /// <summary>Inches → millimetres.</summary>
    public static double InchesToMm(double inches) => inches * MmPerInch;
}
