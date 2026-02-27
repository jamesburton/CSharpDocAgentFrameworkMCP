using System.Text;
using System.Text.Json;
using DocAgent.Core;

namespace DocAgent.McpServer.Serialization;

/// <summary>
/// Minimal hand-rolled TRON serializer for the 5 fixed MCP response shapes.
/// TRON (Token Reduced Object Notation) defines schemas to avoid repeating property names,
/// ideal for LLM token efficiency.
///
/// Format: {"$schema":["field1","field2",...],"data":[[v1,v2,...],[v1,v2,...],...]}
/// </summary>
public static class TronSerializer
{
    private static readonly JsonWriterOptions s_writerOptions = new() { SkipValidation = false };

    /// <summary>
    /// Serialize search results.
    /// Schema: [id, score, kind, displayName, snippet]
    /// </summary>
    public static string SerializeSearchResults(IReadOnlyList<SearchResultItem> items)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, s_writerOptions);

        writer.WriteStartObject();
        WriteSchema(writer, ["id", "score", "kind", "displayName", "snippet"]);
        writer.WriteStartArray("data");
        foreach (var item in items)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(item.Id.Value);
            writer.WriteNumberValue(Math.Round(item.Score, 4));
            writer.WriteStringValue(item.Kind.ToString());
            writer.WriteStringValue(item.DisplayName);
            writer.WriteStringValue(item.Snippet);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Serialize symbol detail.
    /// Schema: [id, kind, displayName, fqn, accessibility, summary, parentId, childIds, relatedIds]
    /// </summary>
    public static string SerializeSymbolDetail(SymbolDetail detail)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, s_writerOptions);

        writer.WriteStartObject();
        WriteSchema(writer, ["id", "kind", "displayName", "fqn", "accessibility", "summary", "parentId", "childIds", "relatedIds"]);
        writer.WriteStartArray("data");

        writer.WriteStartArray();
        writer.WriteStringValue(detail.Node.Id.Value);
        writer.WriteStringValue(detail.Node.Kind.ToString());
        writer.WriteStringValue(detail.Node.DisplayName);
        writer.WriteStringValue(detail.Node.FullyQualifiedName ?? string.Empty);
        writer.WriteStringValue(detail.Node.Accessibility.ToString());
        writer.WriteStringValue(detail.Node.Docs?.Summary ?? string.Empty);
        writer.WriteStringValue(detail.ParentId?.Value ?? string.Empty);

        writer.WriteStartArray();
        foreach (var child in detail.ChildIds)
            writer.WriteStringValue(child.Value);
        writer.WriteEndArray();

        writer.WriteStartArray();
        foreach (var related in detail.RelatedIds)
            writer.WriteStringValue(related.Value);
        writer.WriteEndArray();

        writer.WriteEndArray();

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Serialize reference edges.
    /// Schema: [fromId, toId, edgeKind]
    /// </summary>
    public static string SerializeReferences(IReadOnlyList<SymbolEdge> edges)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, s_writerOptions);

        writer.WriteStartObject();
        WriteSchema(writer, ["fromId", "toId", "edgeKind"]);
        writer.WriteStartArray("data");
        foreach (var edge in edges)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(edge.From.Value);
            writer.WriteStringValue(edge.To.Value);
            writer.WriteStringValue(edge.Kind.ToString());
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Serialize diff entries.
    /// Schema: [id, changeKind, summary]
    /// </summary>
    public static string SerializeDiff(GraphDiff diff)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, s_writerOptions);

        writer.WriteStartObject();
        WriteSchema(writer, ["id", "changeKind", "summary"]);
        writer.WriteStartArray("data");
        foreach (var entry in diff.Entries)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(entry.Id.Value);
            writer.WriteStringValue(entry.ChangeKind.ToString());
            writer.WriteStringValue(entry.Summary);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Serialize project overview (generic object — serialized as-is with TRON wrapper).
    /// Schema: [section, data]
    /// </summary>
    public static string SerializeProjectOverview(object overview)
    {
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, s_writerOptions);

        writer.WriteStartObject();
        WriteSchema(writer, ["section", "data"]);
        writer.WritePropertyName("overview");
        JsonSerializer.Serialize(writer, overview);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteSchema(Utf8JsonWriter writer, string[] fields)
    {
        writer.WriteStartArray("$schema");
        foreach (var field in fields)
            writer.WriteStringValue(field);
        writer.WriteEndArray();
    }
}
