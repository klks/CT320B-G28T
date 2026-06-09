namespace CT320B.UsbApi.Models;

/// <summary>
/// Measurement unit for dimensional TSPL commands (SIZE, GAP, OFFSET, BLINE...).
/// The original DLL emits either a unit-less metric form (e.g. <c>SIZE %.2f,%.2f</c>, mm)
/// or a unit-suffixed form (e.g. <c>SIZE %.0f %s,%.0f %s</c>) depending on this selection.
/// Exact selection logic is confirmed in Phase 1; see docs/protocol_internal.md.
/// </summary>
public enum MeasureUnit
{
    /// <summary>Millimetres — default metric form, e.g. <c>SIZE 60.00,40.00</c>.</summary>
    Millimeter,

    /// <summary>Inches — unit-suffixed form with "inch".</summary>
    Inch,

    /// <summary>Dots — unit-suffixed form with "dot".</summary>
    Dot,
}

/// <summary>Print direction for the TSPL <c>DIRECTION x,y</c> command.</summary>
public enum PrintDirection
{
    /// <summary>DIRECTION 0 — normal feed orientation.</summary>
    Normal = 0,

    /// <summary>DIRECTION 1 — rotated 180°.</summary>
    Reversed = 1,
}
