using System.Globalization;

namespace CT320B.LabelDesigner.Core.Model;

/// <summary>The measurement unit used for display/entry. The model always stores millimetres; this only
/// affects how sizes are shown and typed (Phase 14d).</summary>
public enum MeasurementUnit { Millimeters, Inches }

/// <summary>
/// Converts and formats lengths between the model's storage unit (millimetres) and a display unit
/// (mm or inch). Inches use 3 decimals, millimetres 1, matching typical label-tool precision. Parsing
/// is invariant-culture and lenient (ignores a trailing unit suffix).
/// </summary>
public static class UnitFormat
{
    /// <summary>Millimetres per inch.</summary>
    public const float MmPerInch = 25.4f;

    /// <summary>The short suffix for the unit (<c>mm</c> / <c>in</c>).</summary>
    public static string Suffix(MeasurementUnit unit) => unit == MeasurementUnit.Inches ? "in" : "mm";

    /// <summary>Decimal places shown for the unit.</summary>
    public static int Decimals(MeasurementUnit unit) => unit == MeasurementUnit.Inches ? 3 : 1;

    /// <summary>A sensible spinner step for the unit (0.5 mm / 0.05 in).</summary>
    public static decimal Increment(MeasurementUnit unit) => unit == MeasurementUnit.Inches ? 0.05m : 0.5m;

    /// <summary>Converts a stored millimetre value to the display unit.</summary>
    public static float ToDisplay(float mm, MeasurementUnit unit) =>
        unit == MeasurementUnit.Inches ? mm / MmPerInch : mm;

    /// <summary>Converts a display-unit value back to stored millimetres.</summary>
    public static float ToMm(float value, MeasurementUnit unit) =>
        unit == MeasurementUnit.Inches ? value * MmPerInch : value;

    /// <summary>Formats a stored millimetre value in the display unit, optionally with the suffix.</summary>
    public static string Format(float mm, MeasurementUnit unit, bool withSuffix = true)
    {
        float v = ToDisplay(mm, unit);
        string num = v.ToString("0." + new string('#', Decimals(unit)), CultureInfo.InvariantCulture);
        return withSuffix ? $"{num} {Suffix(unit)}" : num;
    }

    /// <summary>Parses a display-unit string (with or without a unit suffix) to millimetres; returns
    /// false on malformed input.</summary>
    public static bool TryParseToMm(string? text, MeasurementUnit unit, out float mm)
    {
        mm = 0f;
        if (string.IsNullOrWhiteSpace(text)) return false;
        string s = text.Trim();
        // Honour an explicit suffix if present, overriding the requested unit.
        MeasurementUnit effective = unit;
        if (s.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) { effective = MeasurementUnit.Millimeters; s = s[..^2]; }
        else if (s.EndsWith("in", StringComparison.OrdinalIgnoreCase)) { effective = MeasurementUnit.Inches; s = s[..^2]; }
        else if (s.EndsWith('"')) { effective = MeasurementUnit.Inches; s = s[..^1]; }
        if (!float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return false;
        mm = ToMm(v, effective);
        return true;
    }
}
