using System.Xml.Linq;
using DocAgent.Core;

namespace DocAgent.Ingestion;

/// <summary>
/// Parses a single symbol's XML doc comment string into a <see cref="DocComment"/> record.
/// Uses System.Xml.Linq for structured extraction with best-effort recovery on malformed input.
/// </summary>
public sealed class XmlDocParser
{
    /// <summary>
    /// Parse a single symbol's XML doc comment into a DocComment.
    /// <paramref name="xmlContent"/> is the raw XML from ISymbol.GetDocumentationCommentXml().
    /// Returns null if xmlContent is null or whitespace (caller handles placeholder synthesis).
    /// </summary>
    public DocComment? Parse(string? xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            return null;

        XElement root;
        bool hasParseWarning = false;

        try
        {
            var doc = XDocument.Parse(xmlContent);
            // Roslyn wraps in <member name="...">...</member>; handle both wrapped and bare forms.
            root = doc.Root?.Name.LocalName == "member"
                ? doc.Root
                : doc.Root ?? new XElement("member");
        }
        catch (System.Xml.XmlException)
        {
            // First recovery attempt: wrap in a root element
            try
            {
                var doc = XDocument.Parse($"<doc>{xmlContent}</doc>");
                root = doc.Root!;
                hasParseWarning = true;
            }
            catch (System.Xml.XmlException)
            {
                // Completely unparseable — return raw content in Summary with warning prefix
                return new DocComment(
                    Summary: $"[Parse warning] {xmlContent}",
                    Remarks: null,
                    Params: new Dictionary<string, string>(),
                    TypeParams: new Dictionary<string, string>(),
                    Returns: null,
                    Examples: Array.Empty<string>(),
                    Exceptions: Array.Empty<(string, string)>(),
                    SeeAlso: Array.Empty<string>());
            }
        }

        var summary = ExtractInnerText(root.Element("summary"));
        var remarks = ExtractInnerText(root.Element("remarks"));
        var returns = ExtractInnerText(root.Element("returns"));

        if (hasParseWarning && summary != null)
            summary = $"[Parse warning] {summary}";
        else if (hasParseWarning && summary == null)
            summary = "[Parse warning]";

        var @params = root.Elements("param")
            .Where(e => e.Attribute("name")?.Value is not null)
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => ExtractInnerText(e) ?? string.Empty);

        var typeParams = root.Elements("typeparam")
            .Where(e => e.Attribute("name")?.Value is not null)
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => ExtractInnerText(e) ?? string.Empty);

        var examples = root.Elements("example")
            .Select(e => ExtractInnerText(e) ?? string.Empty)
            .ToList();

        var exceptions = root.Elements("exception")
            .Select(e => (
                Type: e.Attribute("cref")?.Value ?? string.Empty,
                Description: ExtractInnerText(e) ?? string.Empty))
            .ToList();

        var seeAlso = root.Elements("seealso")
            .Select(e => e.Attribute("cref")?.Value ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

        return new DocComment(
            Summary: summary,
            Remarks: remarks,
            Params: @params,
            TypeParams: typeParams,
            Returns: returns,
            Examples: examples,
            Exceptions: exceptions,
            SeeAlso: seeAlso);
    }

    /// <summary>
    /// Extracts inner text from an element, collapsing inline XML elements (like &lt;see&gt;) to their text content.
    /// Returns null if the element is null or produces only whitespace.
    /// </summary>
    private static string? ExtractInnerText(XElement? element)
    {
        if (element is null)
            return null;

        // Collect all text nodes recursively, which handles inline <see cref="..."/> etc.
        var text = string.Concat(element.DescendantNodes()
            .OfType<XText>()
            .Select(t => t.Value));

        // Normalize whitespace: collapse runs of whitespace to single space, trim ends
        var normalized = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
