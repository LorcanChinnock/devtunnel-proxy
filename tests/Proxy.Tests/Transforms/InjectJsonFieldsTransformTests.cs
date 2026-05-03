using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevTunnelProxy.Transforms;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Transforms;

namespace Proxy.Tests.Transforms;

public class InjectJsonFieldsTransformTests
{
    [Fact]
    public async Task Skips_when_content_type_is_null()
    {
        var ctx = BuildContext(body: "{}", contentType: null);
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["x"] = "1" });

        await transform.ApplyAsync(ctx);

        AssertBodyUnchanged(ctx);
    }

    [Fact]
    public async Task Skips_when_content_type_is_text_plain()
    {
        var ctx = BuildContext(body: "{}", contentType: "text/plain");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["x"] = "1" });

        await transform.ApplyAsync(ctx);

        AssertBodyUnchanged(ctx);
    }

    [Fact]
    public async Task Skips_when_content_length_is_zero()
    {
        var ctx = BuildContext(body: string.Empty, contentType: "application/json", contentLength: 0);
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["x"] = "1" });

        await transform.ApplyAsync(ctx);

        AssertBodyUnchanged(ctx);
    }

    [Fact]
    public async Task Skips_when_body_is_whitespace()
    {
        var ctx = BuildContext(body: "   \n\t", contentType: "application/json");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["x"] = "1" });

        await transform.ApplyAsync(ctx);

        AssertBodyUnchanged(ctx);
    }

    [Fact]
    public async Task Skips_when_body_is_malformed_json()
    {
        var ctx = BuildContext(body: "not-json", contentType: "application/json");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["x"] = "1" });

        await transform.ApplyAsync(ctx);

        AssertBodyUnchanged(ctx);
    }

    [Fact]
    public async Task Skips_when_root_is_json_array()
    {
        var ctx = BuildContext(body: "[1,2,3]", contentType: "application/json");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["x"] = "1" });

        await transform.ApplyAsync(ctx);

        AssertBodyUnchanged(ctx);
    }

    [Fact]
    public async Task Skips_when_root_is_json_scalar()
    {
        var ctx = BuildContext(body: "42", contentType: "application/json");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["x"] = "1" });

        await transform.ApplyAsync(ctx);

        AssertBodyUnchanged(ctx);
    }

    [Fact]
    public async Task Honours_application_json_with_charset()
    {
        var ctx = BuildContext(body: "{\"x\":1}", contentType: "application/json; charset=utf-8");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["y"] = "2" });

        await transform.ApplyAsync(ctx);

        var merged = await ReadContent(ctx);
        var obj = JsonNode.Parse(merged)!.AsObject();
        Assert.Equal(1, (int)obj["x"]!);
        Assert.Equal(2, (int)obj["y"]!);
    }

    [Fact]
    public async Task Skips_application_problem_plus_json_due_to_substring_contains_semantics()
    {
        // Pins current behaviour: ContentType.Contains("application/json") is a plain substring check,
        // so "application/problem+json" does not match. Any change here should be intentional.
        var ctx = BuildContext(body: "{\"x\":1}", contentType: "application/problem+json");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["y"] = "2" });

        await transform.ApplyAsync(ctx);

        AssertBodyUnchanged(ctx);
    }

    [Fact]
    public async Task Injects_parsed_field_values_with_correct_types()
    {
        var ctx = BuildContext(body: "{}", contentType: "application/json");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string>
        {
            ["b"] = "true",
            ["n"] = "42",
            ["o"] = "{\"a\":1}",
            ["s"] = "not json",
        });

        await transform.ApplyAsync(ctx);

        var merged = await ReadContent(ctx);
        var obj = JsonNode.Parse(merged)!.AsObject();

        Assert.Equal(JsonValueKind.True, obj["b"]!.GetValue<JsonElement>().ValueKind);
        Assert.Equal(42, (int)obj["n"]!);
        Assert.Equal(1, (int)obj["o"]!["a"]!);
        Assert.Equal("not json", (string)obj["s"]!);
    }

    [Fact]
    public async Task Overwrites_existing_keys_with_injected_values()
    {
        var ctx = BuildContext(body: "{\"key\":\"old\"}", contentType: "application/json");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["key"] = "\"new\"" });

        await transform.ApplyAsync(ctx);

        var merged = await ReadContent(ctx);
        var obj = JsonNode.Parse(merged)!.AsObject();
        Assert.Equal("new", (string)obj["key"]!);
    }

    [Fact]
    public async Task Replaces_request_body_with_merged_stream_at_position_zero()
    {
        var ctx = BuildContext(body: "{\"x\":1}", contentType: "application/json");
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["y"] = "2" });

        await transform.ApplyAsync(ctx);

        Assert.Equal(0, ctx.HttpContext.Request.Body.Position);
        Assert.Equal(ctx.HttpContext.Request.Body.Length, ctx.HttpContext.Request.ContentLength);
    }

    [Fact]
    public async Task Updates_proxy_request_content_length_header_when_content_present()
    {
        var ctx = BuildContext(body: "{\"x\":1}", contentType: "application/json");
        ctx.ProxyRequest.Content = new ByteArrayContent([0, 0, 0, 0, 0, 0, 0]);
        var transform = new InjectJsonFieldsTransform(new Dictionary<string, string> { ["y"] = "2" });

        await transform.ApplyAsync(ctx);

        Assert.Equal(ctx.HttpContext.Request.Body.Length, ctx.ProxyRequest.Content.Headers.ContentLength);
    }

    private static RequestTransformContext BuildContext(string body, string? contentType, long? contentLength = null)
    {
        var http = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(body);
        var stream = new MemoryStream(bytes);
        http.Request.Body = stream;
        http.Request.ContentType = contentType;
        http.Request.ContentLength = contentLength ?? bytes.Length;

        var ctx = new RequestTransformContext
        {
            HttpContext = http,
            ProxyRequest = new HttpRequestMessage(),
            Path = http.Request.Path,
            Query = new QueryTransformContext(http.Request),
            HeadersCopied = false,
        };
        ctx.HttpContext.Items["__originalBody"] = (Stream)stream;
        return ctx;
    }

    private static async Task<string> ReadContent(RequestTransformContext ctx)
    {
        Assert.Null(ctx.ProxyRequest.Content);
        ctx.HttpContext.Request.Body.Position = 0;
        using var reader = new StreamReader(ctx.HttpContext.Request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static void AssertBodyUnchanged(RequestTransformContext ctx)
    {
        Assert.Null(ctx.ProxyRequest.Content);
        var original = (Stream)ctx.HttpContext.Items["__originalBody"]!;
        Assert.Same(original, ctx.HttpContext.Request.Body);
        Assert.Equal(0, ctx.HttpContext.Request.Body.Position);
    }
}
