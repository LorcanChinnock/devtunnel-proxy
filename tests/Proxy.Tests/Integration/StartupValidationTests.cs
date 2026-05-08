namespace Proxy.Tests.Integration;

public class StartupValidationTests
{
    [Fact]
    public async Task Boot_throws_when_credentials_with_wildcard_origin()
    {
        await using var upstream = await EchoUpstreamServer.StartAsync();
        await using var factory = new ProxyAppFactory(
            upstream.BaseUrl,
            extraConfig: new Dictionary<string, string?>
            {
                ["Proxies:test:Cors:Policies:bad:AllowedOrigins:0"] = "*",
                ["Proxies:test:Cors:Policies:bad:AllowCredentials"] = "true",
            });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/");
        });

        Assert.Contains("bad", ex.Message);
    }
}
