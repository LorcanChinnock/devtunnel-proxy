using System.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace AppHost;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var directory = builder.Configuration["Proxies:Directory"]
            ?? Path.Combine(builder.AppHostDirectory, "proxies");

        var files = Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : [];

        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"No proxy configurations found in '{directory}'. Add at least one <slug>.json file.");
        }

        VerifyDevtunnelLogin();
        VerifyDevtunnelTokenCache();

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!ProxySlug.IsValid(name))
            {
                throw new InvalidOperationException(
                    $"Proxy config filename '{Path.GetFileName(file)}' is not a valid devtunnel slug " +
                    "(lowercase letters, digits, hyphens; 1-32 chars; must start and end alphanumeric).");
            }

            var anonymous = ProxyConfigFile.LoadAnonymousAccess(file);

            var proxy = builder.AddProject<Projects.Proxy>($"proxy-{name}")
                               .WithEndpoint("https", e => { e.Port = null; e.TargetPort = null; })
                               .WithEnvironment("Proxy__ConfigFile", file);

            var tunnel = builder.AddDevTunnel($"tunnel-{name}", tunnelId: name)
                                .WithReference(proxy);

            if (anonymous)
            {
                tunnel.WithAnonymousAccess();
            }
        }

        builder.Build().Run();
    }

    private static void VerifyDevtunnelLogin()
    {
        string output;
        try
        {
            var psi = new ProcessStartInfo("devtunnel", "user show")
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

        if (!output.Contains("Login token expired", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("Not logged in", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            "The devtunnel CLI is not authenticated (login token expired or missing). " +
            "Run 'devtunnel user login -g' (GitHub) or 'devtunnel user login -d' (Microsoft) before starting the AppHost.");
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
