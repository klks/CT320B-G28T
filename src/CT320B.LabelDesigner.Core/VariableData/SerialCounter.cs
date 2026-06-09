using System.Globalization;

namespace CT320B.LabelDesigner.Core.VariableData;

/// <summary>
/// An auto-incrementing data source for batch printing. Each counter contributes a named token (its
/// <see cref="Name"/>) that fields reference as <c>{name}</c>; for row <c>i</c> (0-based) the token
/// resolves to <see cref="ValueAt"/>(<c>i</c>) — i.e. <see cref="Start"/> + <c>i</c>×<see cref="Step"/>,
/// left-padded to <see cref="Padding"/> digits and wrapped in <see cref="Prefix"/>/<see cref="Suffix"/>.
/// Counters live on the <see cref="Model.LabelDocument"/> so they persist with the design.
/// </summary>
public sealed class SerialCounter
{
    /// <summary>The token name fields reference as <c>{name}</c> (e.g. <c>sn</c>). Case-insensitive when
    /// resolved; whitespace is trimmed.</summary>
    public string Name { get; set; } = "sn";

    /// <summary>The value for the first label (row 0).</summary>
    public long Start { get; set; } = 1;

    /// <summary>The increment added per label. May be negative.</summary>
    public long Step { get; set; } = 1;

    /// <summary>Minimum number of digits; the number is left-padded with zeros to this width (0 = none).
    /// The sign and prefix/suffix are not counted.</summary>
    public int Padding { get; set; }

    /// <summary>Text prepended to the formatted number.</summary>
    public string Prefix { get; set; } = "";

    /// <summary>Text appended to the formatted number.</summary>
    public string Suffix { get; set; } = "";

    /// <summary>The token value for the given 0-based row index.</summary>
    public string ValueAt(int index)
    {
        long value = Start + (long)index * Step;
        string digits;
        if (value < 0)
            digits = "-" + (-value).ToString(CultureInfo.InvariantCulture)
                .PadLeft(Math.Max(0, Padding), '0');
        else
            digits = value.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(0, Padding), '0');
        return Prefix + digits + Suffix;
    }
}
