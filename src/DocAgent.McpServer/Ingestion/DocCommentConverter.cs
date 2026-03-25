using System.Text.Json;
using System.Text.Json.Serialization;
using DocAgent.Core;

namespace DocAgent.McpServer.Ingestion;

/// <summary>
/// Custom JSON converter for <see cref="DocComment"/> that handles shape mismatches between
/// the TypeScript sidecar output and the C# domain record.
/// <para>
/// TS shape → C# shape mappings:
/// <list type="bullet">
///   <item><c>example: string|null</c> → <c>Examples: IReadOnlyList&lt;string&gt;</c> (wraps single string in list, null becomes empty)</item>
///   <item><c>throws: Record&lt;string,string&gt;</c> → <c>Exceptions: IReadOnlyList&lt;(string Type, string Description)&gt;</c></item>
///   <item><c>see: string[]</c> → <c>SeeAlso: IReadOnlyList&lt;string&gt;</c></item>
///   <item><c>params</c>, <c>typeParams</c>, <c>summary</c>, <c>remarks</c>, <c>returns</c> map directly</item>
/// </list>
/// </para>
/// </summary>
public sealed class DocCommentConverter : JsonConverter<DocComment>
{
    public override DocComment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var summary = ReadNullableString(root, "summary");
        var remarks = ReadNullableString(root, "remarks");
        var returns = ReadNullableString(root, "returns");

        // params: Record<string, string>
        var @params = ReadStringDictionary(root, "params");

        // typeParams: Record<string, string>
        var typeParams = ReadStringDictionary(root, "typeParams");

        // example: string | null  →  Examples: IReadOnlyList<string>
        List<string> examples;
        if (root.TryGetProperty("example", out var exampleProp) && exampleProp.ValueKind == JsonValueKind.String)
        {
            var exampleStr = exampleProp.GetString();
            examples = exampleStr is not null ? [exampleStr] : [];
        }
        else
        {
            examples = [];
        }

        // throws: Record<string, string>  →  Exceptions: IReadOnlyList<(string Type, string Description)>
        var exceptions = new List<(string Type, string Description)>();
        if (root.TryGetProperty("throws", out var throwsProp) && throwsProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in throwsProp.EnumerateObject())
            {
                var description = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : string.Empty;
                exceptions.Add((property.Name, description));
            }
        }

        // see: string[]  →  SeeAlso: IReadOnlyList<string>
        var seeAlso = new List<string>();
        if (root.TryGetProperty("see", out var seeProp) && seeProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in seeProp.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var s = element.GetString();
                    if (s is not null) seeAlso.Add(s);
                }
            }
        }

        return new DocComment(
            Summary: summary,
            Remarks: remarks,
            Params: @params,
            TypeParams: typeParams,
            Returns: returns,
            Examples: examples.AsReadOnly(),
            Exceptions: exceptions.AsReadOnly(),
            SeeAlso: seeAlso.AsReadOnly());
    }

    public override void Write(Utf8JsonWriter writer, DocComment value, JsonSerializerOptions options)
    {
        throw new NotSupportedException("DocCommentConverter is read-only (sidecar deserialization only).");
    }

    private static string? ReadNullableString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string> ReadStringDictionary(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in prop.EnumerateObject())
            {
                if (kvp.Value.ValueKind == JsonValueKind.String)
                {
                    dict[kvp.Name] = kvp.Value.GetString() ?? string.Empty;
                }
            }
            return dict;
        }
        return new Dictionary<string, string>();
    }
}
