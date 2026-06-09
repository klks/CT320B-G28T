using System.Text.Json;
using System.Text.Json.Serialization;
using CT320B.LabelDesigner.Core.Model;

namespace CT320B.LabelDesigner.Core.Serialization;

/// <summary>
/// Persists a <see cref="LabelDocument"/> to/from JSON using <c>System.Text.Json</c> with
/// polymorphic elements (the <c>"type"</c> discriminator declared on <see cref="LabelElement"/>).
/// Documents (<c>.ct320b.json</c>) and templates share this schema.
/// </summary>
public static class LabelJson
{
    /// <summary>The shared serializer options (camelCase, indented, enums-as-strings, hex colors).</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ColorJsonConverter());
        return options;
    }

    /// <summary>Serializes a document to a JSON string.</summary>
    public static string Serialize(LabelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options);
    }

    /// <summary>Deserializes a document from a JSON string.</summary>
    public static LabelDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return JsonSerializer.Deserialize<LabelDocument>(json, Options)
               ?? throw new JsonException("Deserialized to a null document.");
    }

    /// <summary>Deep-clones a document via a JSON round-trip — used to instantiate a template or a
    /// bundled sample without mutating the original.</summary>
    public static LabelDocument Clone(LabelDocument document) => Deserialize(Serialize(document));

    /// <summary>Writes a document to a file as JSON.</summary>
    public static void Save(LabelDocument document, string path) =>
        File.WriteAllText(path, Serialize(document));

    /// <summary>Reads a document from a JSON file.</summary>
    public static LabelDocument Load(string path) =>
        Deserialize(File.ReadAllText(path));
}
