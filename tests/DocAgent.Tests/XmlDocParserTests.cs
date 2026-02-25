using DocAgent.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests;

public class XmlDocParserTests
{
    [Fact]
    public void Parse_returns_dictionary_by_assembly_name()
    {
        var parser = new XmlDocParser();
        var set = parser.Parse(new[] { ("MyAsm", "<doc/>") });

        set.XmlDocsByAssemblyName.Should().ContainKey("MyAsm");
        set.XmlDocsByAssemblyName["MyAsm"].Should().Contain("<doc/>");
    }
}
