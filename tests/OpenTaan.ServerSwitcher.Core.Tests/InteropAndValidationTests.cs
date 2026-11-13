using OpenTaan.ServerSwitcher.Core.Format;
using OpenTaan.ServerSwitcher.Core.Interop;
using OpenTaan.ServerSwitcher.Core.Model;
using OpenTaan.ServerSwitcher.Core.Validation;

namespace OpenTaan.ServerSwitcher.Core.Tests;

public class InteropTests
{
    [StockFileFact]
    public void Xml_StockFile_LooksNatural_AndRoundTrips()
    {
        var root = OmFile.Decode(Fixtures.TaanServer).Tree.Root;
        var xml = XmlProjection.ToXml(root);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);
        Assert.Contains("<server name=\"Vietnam-Service\">", xml);
        Assert.Contains("<channel name=\"ch-1\" ip=\"180.148.129.2\" port=\"2180\" />", xml);
        Assert.DoesNotContain("_om.", xml);

        Assert.True(XmlProjection.FromXml(xml).StructurallyEquals(root));
    }

    [Fact]
    public void Xml_RoundTripsThingsXmlCannotExpressDirectly()
    {
        var root = new ConfigNode("root", "some metadata");
        var odd = root.AddChild(new ConfigNode("has space"));
        odd.AddAttribute("dup", "1");
        odd.AddAttribute("dup", "2 <&> \"quoted\"\nnewline");
        root.AddChild(new ConfigNode(""));
        var bad = root.AddChild(new ConfigNode("ok"));
        bad.AddAttribute("1starts-with-digit", "x");
        root.AddChild(new ConfigNode("_om.node"));

        var xml = XmlProjection.ToXml(root);
        Assert.True(XmlProjection.FromXml(xml).StructurallyEquals(root), xml);
    }

    [Fact]
    public void Xml_RejectsTextContentWithLineNumber()
    {
        var ex = Assert.Throws<FormatException>(() => XmlProjection.FromXml("<a>\n<b>hello</b></a>"));
        Assert.Contains("Line 2", ex.Message);
    }

    [Fact]
    public void Xml_RejectsMalformedXml()
    {
        Assert.Throws<FormatException>(() => XmlProjection.FromXml("<a><b></a>"));
    }

    [StockFileFact]
    public void Json_UsesExpectedLayout_AndRoundTrips()
    {
        var root = OmFile.Decode(Fixtures.TaanServer).Tree.Root;
        var json = JsonProjection.ToJson(root);

        Assert.Contains("\"name\": \"content\"", json);
        Assert.Contains("\"key\": \"ip\"", json);
        Assert.True(JsonProjection.FromJson(json).StructurallyEquals(root));
    }

    [Fact]
    public void Json_ReadsCompactLayout()
    {
        // Compact layout: no attributes/children keys when empty, non-ASCII kept.
        const string json = """
            {
              "name": "content",
              "metadata": "",
              "children": [
                { "name": "server", "metadata": "", "attributes": [ { "key": "name", "value": "Việt Nam" } ] },
                { "name": "EnableSoul", "metadata": "" }
              ]
            }
            """;
        var root = JsonProjection.FromJson(json);
        Assert.Equal("Việt Nam", root.Children[0].GetValue("name"));
        Assert.Empty(root.Children[1].Attributes);
    }

    [Fact]
    public void Json_ReportsWhereItIsWrong()
    {
        var ex = Assert.Throws<FormatException>(() => JsonProjection.FromJson("""{ "name": "a", "children": [ { "metadata": "" } ] }"""));
        Assert.Contains("$.children[0]", ex.Message);
    }
}

public class ValidationTests
{
    [Theory]
    [InlineData("180.148.129.2", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("256.1.1.1", false)]
    [InlineData("1.2.3", false)]
    [InlineData("01.2.3.4", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("", false)]
    [InlineData("1", false)]
    public void IsIPv4(string value, bool expected) => Assert.Equal(expected, IpAddressRule.IsIPv4(value));

    [Theory]
    [InlineData("2180", true)]
    [InlineData("1", true)]
    [InlineData("65535", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("-1", false)]
    [InlineData("21a", false)]
    [InlineData("", false)]
    public void IsPort(string value, bool expected) => Assert.Equal(expected, PortRule.IsPort(value));

    [StockFileFact]
    public void StockFile_HasNoProblems()
    {
        var root = OmFile.Decode(Fixtures.TaanServer).Tree.Root;
        Assert.Empty(Validator.Default.Validate(root));
    }

    [Fact]
    public void ReportsBadIpPortAndDuplicates()
    {
        var root = Fixtures.SampleTree();
        var channels = root.Children[0].Children;
        channels[0].FindAttribute("ip")!.Value = "300.1.1.1";
        channels[1].FindAttribute("port")!.Value = "abc";
        channels[2].FindAttribute("ip")!.Value = "game.example.com";
        channels[2].AddAttribute("port", "2181");

        var problems = Validator.Default.Validate(root);

        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Error && p.Node == channels[0] && p.Attribute!.Key == "ip");
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Error && p.Node == channels[1] && p.Attribute!.Key == "port");
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Warning && p.Node == channels[2] && p.Message.Contains("host name"));
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Warning && p.Message.Contains("Duplicate"));
        Assert.Equal(2, problems.Count(p => p.Severity == ProblemSeverity.Error));
    }
}
