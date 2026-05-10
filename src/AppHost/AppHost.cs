using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace AppHost;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var proxyDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "Proxy"));
        var env = builder.Environment.EnvironmentName;

        var proxyConfig = new ConfigurationBuilder()
            .SetBasePath(proxyDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{env}.json", optional: true)
            .Build();

        var slugs = proxyConfig.GetSection("Proxies").GetChildren().ToArray();
        if (slugs.Length == 0)
        {
            throw new InvalidOperationException(
                $"No proxies defined under 'Proxies' in {proxyDir}/appsettings.json " +
                $"or appsettings.{env}.json. Add at least one entry: " +
                "\"Proxies\": { \"<slug>\": { ... } }.");
        }

        VerifyDevtunnelTokenCache();

        foreach (var section in slugs)
        {
            var name = section.Key;
            if (!ProxySlug.IsValid(name))
            {
                throw new InvalidOperationException(
                    $"Proxy slug '{name}' is not a valid devtunnel slug " +
                    "(lowercase letters, digits, hyphens; 1-32 chars; must start and end alphanumeric).");
            }

            var anonymous = section.GetValue("DevTunnel:AnonymousAccess", true);
            var port = section.GetValue<int?>("Port");

            var proxy = builder.AddProject<Projects.Proxy>($"proxy-{name}")
                               .WithEndpoint("https", e => { e.Port = port; e.TargetPort = null; })
                               .WithEnvironment("Proxy__Slug", name);

            var tunnel = builder.AddDevTunnel($"tunnel-{name}", tunnelId: name)
                                .WithReference(proxy);

            if (anonymous)
            {
                tunnel.WithAnonymousAccess();
            }
        }

        builder.Build().Run();
    }

    private static void VerifyDevtunnelTokenCache()
    {
        string output;
        try
        {
            var psi = new ProcessStartInfo("devtunnel", "list")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return;
            output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(5_000);
        }
        catch
        {
            return;
        }

        if (!output.Contains("An item with the same key has already been added", StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            "The devtunnel CLI's tunnel-access-token cache is corrupt (duplicate 'host' key). " +
            "Aspire's tunnel creation will fail. " +
            "Fix on macOS: " +
            "security delete-generic-password -s tunnels -a \"https://global.rel.tunnels.api.visualstudio.com/auth/tunnels\" " +
            "(see README Troubleshooting for Linux/Windows).");
    }
}
