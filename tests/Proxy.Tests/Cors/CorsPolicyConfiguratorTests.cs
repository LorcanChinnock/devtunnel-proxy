using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Proxy.Cors;

namespace Proxy.Tests.Cors;

public class CorsPolicyConfiguratorTests
{
    [Fact]
    public void Wildcard_origins_methods_headers_map_to_allow_any()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["p:AllowedOrigins:0"] = "*",
            ["p:AllowedMethods:0"] = "*",
            ["p:AllowedHeaders:0"] = "*",
        });

        var policy = options.GetPolicy("p")!;
        Assert.True(policy.AllowAnyOrigin);
        Assert.True(policy.AllowAnyMethod);
        Assert.True(policy.AllowAnyHeader);
    }

    [Fact]
    public void Explicit_values_map_to_with_lists()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["p:AllowedOrigins:0"] = "https://a.example",
            ["p:AllowedOrigins:1"] = "https://b.example",
            ["p:AllowedMethods:0"] = "GET",
            ["p:AllowedMethods:1"] = "POST",
            ["p:AllowedHeaders:0"] = "X-Custom",
            ["p:ExposedHeaders:0"] = "X-Trace-Id",
        });

        var policy = options.GetPolicy("p")!;
        Assert.False(policy.AllowAnyOrigin);
        Assert.Contains("https://a.example", policy.Origins);
        Assert.Contains("https://b.example", policy.Origins);
        Assert.Contains("GET", policy.Methods);
        Assert.Contains("POST", policy.Methods);
        Assert.Contains("X-Custom", policy.Headers);
        Assert.Contains("X-Trace-Id", policy.ExposedHeaders);
    }

    [Fact]
    public void AllowCredentials_true_enables_credentials()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["p:AllowedOrigins:0"] = "https://a.example",
            ["p:AllowCredentials"] = "true",
        });

        var policy = options.GetPolicy("p")!;
        Assert.True(policy.SupportsCredentials);
    }

    [Fact]
    public void Preflight_max_age_seconds_populates_policy()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["p:AllowedOrigins:0"] = "*",
            ["p:PreflightMaxAgeSeconds"] = "600",
        });

        var policy = options.GetPolicy("p")!;
        Assert.Equal(TimeSpan.FromSeconds(600), policy.PreflightMaxAge);
    }

    [Fact]
    public void Throws_when_credentials_with_wildcard_origin()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Configure(new Dictionary<string, string?>
        {
            ["bad:AllowedOrigins:0"] = "*",
            ["bad:AllowCredentials"] = "true",
        }));

        Assert.Contains("bad", ex.Message);
    }

    [Fact]
    public void Multiple_policies_all_get_registered()
    {
        var options = Configure(new Dictionary<string, string?>
        {
            ["p1:AllowedOrigins:0"] = "*",
            ["p2:AllowedOrigins:0"] = "https://a.example",
        });

        Assert.NotNull(options.GetPolicy("p1"));
        Assert.NotNull(options.GetPolicy("p2"));
    }

    private static CorsOptions Configure(IDictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var options = new CorsOptions();
        CorsPolicyConfigurator.Configure(options, config);
        return options;
    }
}
