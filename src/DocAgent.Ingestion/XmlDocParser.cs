using DocAgent.Core;

namespace DocAgent.Ingestion;

public interface IXmlDocParser
{
    DocInputSet Parse(IEnumerable<(string AssemblyName, string XmlContent)> xmlDocs);
}

public sealed class XmlDocParser : IXmlDocParser
{
    public DocInputSet Parse(IEnumerable<(string AssemblyName, string XmlContent)> xmlDocs)
    {
        // TODO: parse XML and bind to stable symbol identifiers
        // For V1 scaffold we keep raw payloads.
        return new DocInputSet(xmlDocs.ToDictionary(x => x.AssemblyName, x => x.XmlContent));
    }
}
