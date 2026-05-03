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
