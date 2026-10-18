using AegisWAF.Gateway;

namespace AegisWAF.Tests;

public sealed class StructuredPayloadParserTests
{
    private static readonly InspectionLimits Limits = new(64 * 1024, 256 * 1024, 100, 8, 20, 4, 250);

    [Fact]
    public void Json_parser_extracts_nested_values_and_properties()
    {
        var result = StructuredPayloadParser.Parse("application/json", "{\"profile\":{\"name\":\"alice\"},\"roles\":[\"viewer\"]}", Limits);

        Assert.True(result.Success);
        Assert.Contains(result.Values, x => x.Location.Contains("profile.name") && x.Text == "alice");
        Assert.Contains(result.Values, x => x.Location.Contains("roles[0]") && x.Text == "viewer");
    }

    [Fact]
    public void Form_parser_extracts_keys_and_values_after_url_decoding()
    {
        var result = StructuredPayloadParser.Parse("application/x-www-form-urlencoded", "q=1%20union%20select%201&empty=", Limits);

        Assert.True(result.Success);
        Assert.Contains(result.Values, x => x.Location.EndsWith("q") && x.Text.Contains("union select", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Xml_parser_extracts_text_and_attributes_without_resolving_external_entities()
    {
        var result = StructuredPayloadParser.Parse("application/xml", "<root id='x'><item>hello</item><![CDATA[world]]></root>", Limits);

        Assert.True(result.Success);
        Assert.Contains(result.Values, x => x.Text == "x");
        Assert.Contains(result.Values, x => x.Text == "hello");
        Assert.Contains(result.Values, x => x.Text == "world");
    }

    [Fact]
    public void Xml_external_entity_is_rejected()
    {
        var result = StructuredPayloadParser.Parse("text/xml", "<!DOCTYPE root [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><root>&x;</root>", Limits);

        Assert.False(result.Success);
        Assert.Equal("xml_dtd_or_entity", result.ReasonCode);
    }

    [Fact]
    public void Multipart_parser_extracts_field_names_and_filenames_with_part_limit()
    {
        const string boundary = "----aegis";
        var body = $"--{boundary}\r\nContent-Disposition: form-data; name=\"comment\"\r\n\r\nhello\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"payload.txt\"\r\nContent-Type: text/plain\r\n\r\nunion select\r\n--{boundary}--\r\n";
        var result = StructuredPayloadParser.Parse($"multipart/form-data; boundary={boundary}", body, Limits);

        Assert.True(result.Success);
        Assert.Contains(result.Values, x => x.Location.Contains("comment") && x.Text == "hello");
        Assert.Contains(result.Values, x => x.Location.Contains("filename") && x.Text == "payload.txt");
        Assert.Contains(result.Values, x => x.Text.Contains("union select", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parser_rejects_depth_and_member_limits_instead_of_returning_clean()
    {
        var depth = StructuredPayloadParser.Parse("application/json", "{\"a\":{\"b\":{\"c\":1}}}", Limits with { MaxNestingDepth = 2 });
        var members = StructuredPayloadParser.Parse("application/json", "{\"a\":1,\"b\":2,\"c\":3}", Limits with { MaxMembers = 2 });

        Assert.False(depth.Success);
        Assert.Equal("json_limit", depth.ReasonCode);
        Assert.False(members.Success);
        Assert.Equal("json_limit", members.ReasonCode);
    }

    [Fact]
    public void Form_parser_rejects_malformed_percent_encoding()
    {
        var result = StructuredPayloadParser.Parse("application/x-www-form-urlencoded", "q=%ZZ", Limits);

        Assert.False(result.Success);
        Assert.Equal("form_malformed", result.ReasonCode);
    }

    [Fact]
    public void Form_parser_stops_at_member_limit_without_materializing_unbounded_pairs()
    {
        var result = StructuredPayloadParser.Parse("application/x-www-form-urlencoded", "a=1&b=2&c=3", Limits with { MaxMembers = 2 });

        Assert.False(result.Success);
        Assert.Equal("form_limit", result.ReasonCode);
    }
}
