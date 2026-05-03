using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proxy.Tests.Integration;

public class ProxyIntegrationTests
{
    [Fact]
    public async Task Get_forwards_to_upstream_and_returns_body()
    {
        await using var upstream = await EchoUpstreamServer.StartAsync();
        await using var factory = new ProxyAppFactory(upstream.BaseUrl);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/foo/bar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await ReadEcho(response);
        Assert.Equal("GET", echo!.Method);
        Assert.Equal("/foo/bar", echo.Path);
    }

    [Fact]
    public async Task Cors_preflight_AllowAll_returns_allow_origin_wildcard()
    {
        await using var upstream = await EchoUpstreamServer.StartAsync();
        await using var factory = new ProxyAppFactory(upstream.BaseUrl);
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/anything");
        request.Headers.Add("Origin", "https://x.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.True((int)response.StatusCode is >= 200 and < 300, $"Expected 2xx but got {response.StatusCode}");
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Cors_explicit_origin_blocks_unlisted_origin()
    {
        await using var upstream = await EchoUpstreamServer.StartAsync();
        await using var factory = new ProxyAppFactory(upstream.BaseUrl, new Dictionary<string, string?>
        {
            ["Cors:Policies:Strict:AllowedOrigins:0"] = "https://allowed.example",
            ["Cors:Policies:Strict:AllowedMethods:0"] = "*",
            ["Cors:Policies:Strict:AllowedHeaders:0"] = "*",
            ["ReverseProxy:Routes:strict:ClusterId"] = "default",
            ["ReverseProxy:Routes:strict:CorsPolicy"] = "Strict",
            ["ReverseProxy:Routes:strict:Order"] = "-1",
            ["ReverseProxy:Routes:strict:Match:Path"] = "/strict/{**rest}",
        });
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Options, "/strict/foo");
        request.Headers.Add("Origin", "https://blocked.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task InjectJsonField_merges_into_request_body()
    {
        await using var upstream = await EchoUpstreamServer.StartAsync();
        await using var factory = new ProxyAppFactory(upstream.BaseUrl, new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:inject:ClusterId"] = "default",
            ["ReverseProxy:Routes:inject:CorsPolicy"] = "AllowAll",
            ["ReverseProxy:Routes:inject:Order"] = "-1",
            ["ReverseProxy:Routes:inject:Match:Path"] = "/inject/{**rest}",
            ["ReverseProxy:Routes:inject:Metadata:InjectJsonField.tenant"] = "\"acme\"",
            ["ReverseProxy:Routes:inject:Metadata:InjectJsonField.count"] = "3",
            ["ReverseProxy:Routes:inject:Metadata:InjectJsonField.nested"] = "{\"k\":1}",
        });
        using var client = factory.CreateClient();

        using var content = new StringContent("{\"x\":1}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/inject/foo", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await ReadEcho(response);
        var body = JsonNode.Parse(echo!.Body)!.AsObject();
        Assert.Equal(1, (int)body["x"]!);
        Assert.Equal("acme", (string)body["tenant"]!);
        Assert.Equal(3, (int)body["count"]!);
        Assert.Equal(1, (int)body["nested"]!["k"]!);
    }

    [Fact]
    public async Task InjectJsonField_skips_non_json_content()
    {
        await using var upstream = await EchoUpstreamServer.StartAsync();
        await using var factory = new ProxyAppFactory(upstream.BaseUrl, new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:inject:ClusterId"] = "default",
            ["ReverseProxy:Routes:inject:CorsPolicy"] = "AllowAll",
            ["ReverseProxy:Routes:inject:Order"] = "-1",
            ["ReverseProxy:Routes:inject:Match:Path"] = "/inject/{**rest}",
            ["ReverseProxy:Routes:inject:Metadata:InjectJsonField.tenant"] = "\"acme\"",
        });
        using var client = factory.CreateClient();

        using var content = new StringContent("plain body", Encoding.UTF8, "text/plain");
        var response = await client.PostAsync("/inject/foo", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await ReadEcho(response);
        Assert.Equal("plain body", echo!.Body);
    }

    [Fact]
    public async Task InjectJsonField_passthrough_on_malformed_json()
    {
        await using var upstream = await EchoUpstreamServer.StartAsync();
        await using var factory = new ProxyAppFactory(upstream.BaseUrl, new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:inject:ClusterId"] = "default",
            ["ReverseProxy:Routes:inject:CorsPolicy"] = "AllowAll",
            ["ReverseProxy:Routes:inject:Order"] = "-1",
            ["ReverseProxy:Routes:inject:Match:Path"] = "/inject/{**rest}",
            ["ReverseProxy:Routes:inject:Metadata:InjectJsonField.tenant"] = "\"acme\"",
        });
        using var client = factory.CreateClient();

        using var content = new StringContent("not-json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/inject/foo", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await ReadEcho(response);
        Assert.Equal("not-json", echo!.Body);
    }

    private static async Task<EchoPayload?> ReadEcho(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<EchoPayload>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
    }
}
