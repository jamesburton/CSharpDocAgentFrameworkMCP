using DocAgent.Ingestion;
using FluentAssertions;

namespace DocAgent.Tests;

public class XmlDocParserTests
{
    private readonly XmlDocParser _parser = new();

    // ---------------------------------------------------------------------------
    // Basic parsing
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_null_returns_null()
    {
        _parser.Parse(null).Should().BeNull();
    }

    [Fact]
    public void Parse_empty_string_returns_null()
    {
        _parser.Parse("   ").Should().BeNull();
    }

    [Fact]
    public void Parse_summary_returns_trimmed_text()
    {
        var xml = "<member name=\"M:Foo.Bar\"><summary>  Hello world  </summary></member>";
        var result = _parser.Parse(xml);
        result.Should().NotBeNull();
        result!.Summary.Should().Be("Hello world");
    }

    [Fact]
    public void Parse_all_elements_populates_all_fields()
    {
        var xml = """
            <member name="M:Foo.Bar`1.Method``1(System.String)">
              <summary>Does something</summary>
              <remarks>Extra detail</remarks>
              <typeparam name="T">The type param</typeparam>
              <param name="input">The input string</param>
              <returns>A result</returns>
              <example>var x = new Bar();</example>
              <exception cref="T:System.ArgumentException">Bad arg</exception>
              <seealso cref="T:Foo.Other"/>
            </member>
            """;

        var result = _parser.Parse(xml);

        result.Should().NotBeNull();
        result!.Summary.Should().Be("Does something");
        result.Remarks.Should().Be("Extra detail");
        result.Returns.Should().Be("A result");
        result.TypeParams.Should().ContainKey("T").WhoseValue.Should().Be("The type param");
        result.Params.Should().ContainKey("input").WhoseValue.Should().Be("The input string");
        result.Examples.Should().ContainSingle().Which.Should().Be("var x = new Bar();");
        result.Exceptions.Should().ContainSingle()
            .Which.Should().Be(("T:System.ArgumentException", "Bad arg"));
        result.SeeAlso.Should().ContainSingle().Which.Should().Be("T:Foo.Other");
    }

    [Fact]
    public void Parse_multiple_params_all_captured()
    {
        var xml = """
            <member name="M:Foo.Bar.Method(System.Int32,System.String)">
              <param name="count">How many</param>
              <param name="name">The name</param>
            </member>
            """;

        var result = _parser.Parse(xml);

        result!.Params.Should().HaveCount(2);
        result.Params["count"].Should().Be("How many");
        result.Params["name"].Should().Be("The name");
    }

    [Fact]
    public void Parse_nested_xml_elements_extracts_text_content()
    {
        var xml = "<member name=\"T:Foo\"><summary>See <see cref=\"T:Foo.Bar\"/> for details</summary></member>";
        var result = _parser.Parse(xml);
        // Inner text of summary should be extracted; <see> contributes no text nodes
        result!.Summary.Should().Be("See for details");
    }

    [Fact]
    public void Parse_generic_type_cref_parses_correctly()
    {
        var xml = """
            <member name="T:Foo.MyClass">
              <exception cref="T:System.Collections.Generic.Dictionary`2">dict error</exception>
            </member>
            """;

        var result = _parser.Parse(xml);

        result!.Exceptions.Should().ContainSingle()
            .Which.Type.Should().Be("T:System.Collections.Generic.Dictionary`2");
    }

    // ---------------------------------------------------------------------------
    // Malformed XML
    // ---------------------------------------------------------------------------

    [Fact]
    public void Parse_malformed_xml_returns_best_effort_not_exception()
    {
        // Unclosed tag — parser should not throw
        var xml = "<member><summary>Hello</member>";

        var result = _parser.Parse(xml);

        result.Should().NotBeNull("malformed XML must return a DocComment, not null or an exception");
        // Either recovered with a parse warning, or got raw content — either way, no exception
    }

    [Fact]
    public void Parse_malformed_xml_includes_warning_in_summary()
    {
        var xml = "<unclosed>some text";

        var result = _parser.Parse(xml);

        result.Should().NotBeNull();
        result!.Summary.Should().Contain("[Parse warning]");
    }

    // ---------------------------------------------------------------------------
    // InheritDocResolver tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void Resolve_inheritdoc_follows_base()
    {
        var baseXml = "<member name=\"M:Base.Method\"><summary>Base summary</summary></member>";
        var derivedXml = "<member name=\"M:Derived.Method\"><inheritdoc/></member>";

        var resolver = new InheritDocResolver();
        // Empty-string key represents "natural base"
        var result = resolver.Resolve(derivedXml, key => key == "" ? baseXml : null, _parser);

        result.Should().NotBeNull();
        result!.Summary.Should().Be("Base summary");
    }

    [Fact]
    public void Resolve_inheritdoc_with_cref_resolves_explicit_target()
    {
        const string targetId = "M:IFoo.Method";
        var targetXml = "<member name=\"M:IFoo.Method\"><summary>Interface doc</summary></member>";
        var derivedXml = $"<member name=\"M:Foo.Method\"><inheritdoc cref=\"{targetId}\"/></member>";

        var resolver = new InheritDocResolver();
        var result = resolver.Resolve(derivedXml, key => key == targetId ? targetXml : null, _parser);

        result.Should().NotBeNull();
        result!.Summary.Should().Be("Interface doc");
    }

    [Fact]
    public void Resolve_cycle_detection_returns_null()
    {
        // A -> B -> A (cycle)
        const string idA = "M:A.Method";
        const string idB = "M:B.Method";
        var xmlA = $"<member name=\"{idA}\"><inheritdoc cref=\"{idB}\"/></member>";
        var xmlB = $"<member name=\"{idB}\"><inheritdoc cref=\"{idA}\"/></member>";

        var resolver = new InheritDocResolver();

        string? GetXml(string key) => key == idA ? xmlA : key == idB ? xmlB : null;

        // Start resolution from xmlA — should detect cycle and return null
        var result = resolver.Resolve(xmlA, GetXml, _parser);

        result.Should().BeNull("cycle must not produce infinite recursion or a result");
    }

    [Fact]
    public void Resolve_max_depth_returns_null()
    {
        // Create a chain of 5 symbols, resolve with maxDepth=3 — should stop before resolving
        const string id0 = "M:C0.M";
        const string id1 = "M:C1.M";
        const string id2 = "M:C2.M";
        const string id3 = "M:C3.M";
        const string id4 = "M:C4.M";

        string Xml(string self, string next) =>
            $"<member name=\"{self}\"><inheritdoc cref=\"{next}\"/></member>";
        var leafXml = "<member name=\"M:C4.M\"><summary>Leaf</summary></member>";

        var lookup = new Dictionary<string, string>
        {
            [id1] = Xml(id1, id2),
            [id2] = Xml(id2, id3),
            [id3] = Xml(id3, id4),
            [id4] = leafXml,
        };

        var resolver = new InheritDocResolver();
        // maxDepth=3: from id0->id1->id2->id3 hits depth limit before reaching leaf
        var result = resolver.Resolve(Xml(id0, id1), key => lookup.GetValueOrDefault(key), _parser, maxDepth: 3);

        result.Should().BeNull("chain exceeds maxDepth and should return null");
    }
}
