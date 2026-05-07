using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Proxy.Tests.Integration;

internal sealed class ProxyAppFactory : WebApplicationFactory<Program>
{
    private readonly string _upstreamUrl;
    private readonly IDictionary<string, string?> _extraConfig;

    public ProxyAppFactory(string upstreamUrl, IDictionary<string, string?>? extraConfig = null)
    {
        _upstreamUrl = upstreamUrl;
        _extraConfig = extraConfig ?? new Dictionary<string, string?>();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Cors:Policies:AllowAll:AllowedOrigins:0"] = "*",
                ["Cors:Policies:AllowAll:AllowedMethods:0"] = "*",
                ["Cors:Policies:AllowAll:AllowedHeaders:0"] = "*",
                ["Cors:Policies:AllowAll:AllowCredentials"] = "false",
                ["ReverseProxy:Routes:default:ClusterId"] = "default",
                ["ReverseProxy:Routes:default:CorsPolicy"] = "AllowAll",
                ["ReverseProxy:Routes:default:Match:Path"] = "{**catch-all}",
                ["ReverseProxy:Clusters:default:Destinations:primary:Address"] = _upstreamUrl,
            };
            foreach (var kvp in _extraConfig)
            {
                overrides[kvp.Key] = kvp.Value;
            }
            config.AddInMemoryCollection(overrides);
        });
    }
}
