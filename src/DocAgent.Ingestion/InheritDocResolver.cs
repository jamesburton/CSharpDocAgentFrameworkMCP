using DocAgent.Core;

namespace DocAgent.Ingestion;

/// <summary>
/// Resolves &lt;inheritdoc/&gt; references by walking the override/interface chain.
/// </summary>
public sealed class InheritDocResolver
{
    /// <summary>
    /// Resolve inheritdoc for a symbol by walking its override/interface chain.
    /// </summary>
    /// <param name="symbolDocXml">The raw XML that may contain &lt;inheritdoc/&gt;.</param>
    /// <param name="getBaseDocXml">Callback to retrieve the XML doc string for a given doc comment ID.</param>
    /// <param name="parser">The parser used to convert resolved XML into a DocComment.</param>
    /// <param name="maxDepth">Maximum inheritance chain depth to follow before giving up.</param>
    /// <returns>The resolved DocComment, or null if resolution fails or input is null/empty.</returns>
    public DocComment? Resolve(
        string? symbolDocXml,
        Func<string, string?> getBaseDocXml,
        XmlDocParser parser,
        int maxDepth = 10)
    {
        if (string.IsNullOrWhiteSpace(symbolDocXml))
            return null;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        return ResolveInner(symbolDocXml, getBaseDocXml, parser, visited, maxDepth);
    }

    private static DocComment? ResolveInner(
        string? xmlContent,
        Func<string, string?> getBaseDocXml,
        XmlDocParser parser,
        HashSet<string> visited,
        int remainingDepth)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            return null;

        if (remainingDepth <= 0)
            return null;

        // Check if this XML contains an inheritdoc element
        string? inheritdocCref = ExtractInheritdocCref(xmlContent!, out bool hasInheritdoc);

        if (!hasInheritdoc)
        {
            // No inheritdoc — parse directly
            return parser.Parse(xmlContent);
        }

        // Has inheritdoc — need to resolve
        if (inheritdocCref is null)
        {
            // No cref; caller must supply base doc ID via getBaseDocXml with empty string key
            // Convention: pass empty string to signal "get the natural override/interface target"
            var baseCref = string.Empty;

            if (visited.Contains(baseCref))
                return null; // cycle

            visited.Add(baseCref);
            var baseXml = getBaseDocXml(baseCref);
            return ResolveInner(baseXml, getBaseDocXml, parser, visited, remainingDepth - 1);
        }
        else
        {
            // Explicit cref
            if (visited.Contains(inheritdocCref))
                return null; // cycle

            visited.Add(inheritdocCref);
            var baseXml = getBaseDocXml(inheritdocCref);
            return ResolveInner(baseXml, getBaseDocXml, parser, visited, remainingDepth - 1);
        }
    }

    /// <summary>
    /// Checks whether the XML string contains an &lt;inheritdoc/&gt; element
    /// and extracts its cref attribute if present.
    /// Uses lightweight string scanning to avoid XML parse overhead for non-inheritdoc symbols.
    /// </summary>
    private static string? ExtractInheritdocCref(string xmlContent, out bool hasInheritdoc)
    {
        // Quick check before parsing
        if (!xmlContent.Contains("inheritdoc", StringComparison.OrdinalIgnoreCase))
        {
            hasInheritdoc = false;
            return null;
        }

        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xmlContent);
            var root = doc.Root;
            if (root is null) { hasInheritdoc = false; return null; }

            // Check root element itself or its children
            System.Xml.Linq.XElement? inheritdocElement = null;
            if (root.Name.LocalName.Equals("inheritdoc", StringComparison.OrdinalIgnoreCase))
                inheritdocElement = root;
            else
                inheritdocElement = root.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("inheritdoc", StringComparison.OrdinalIgnoreCase));

            if (inheritdocElement is null)
            {
                hasInheritdoc = false;
                return null;
            }

            hasInheritdoc = true;
            return inheritdocElement.Attribute("cref")?.Value;
        }
        catch (System.Xml.XmlException)
        {
            hasInheritdoc = false;
            return null;
        }
    }
}
