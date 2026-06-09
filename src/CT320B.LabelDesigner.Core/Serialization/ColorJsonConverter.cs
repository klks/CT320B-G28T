using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CT320B.LabelDesigner.Core.Serialization;

/// <summary>
/// (De)serializes <see cref="Color"/> as a <c>#AARRGGBB</c> hex string — compact, human-readable,
/// and free of the dozens of computed properties STJ would otherwise emit for the struct. Also
/// accepts <c>#RRGGBB</c> (alpha defaults to opaque).
/// </summary>
public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (string.IsNullOrEmpty(s))
            return Color.Empty;

        ReadOnlySpan<char> hex = s.AsSpan();
        if (hex[0] == '#') hex = hex[1..];

        if (hex.Length == 6)
        {
            int rgb = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
        if (hex.Length == 8)
        {
            uint argb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromArgb(unchecked((int)argb));
        }
        throw new JsonException($"Invalid color '{s}'; expected #RRGGBB or #AARRGGBB.");
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}");
}
