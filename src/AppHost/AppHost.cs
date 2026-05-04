using System.Diagnostics;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var tunnelId = builder.Configuration["DevTunnel:Id"]
    ?? throw new InvalidOperationException("DevTunnel:Id is required in appsettings.json.");

var anonymousAccess = builder.Configuration.GetValue("DevTunnel:AnonymousAccess", true);

VerifyDevtunnelTokenCache(tunnelId);

var proxy = builder.AddProject<Projects.Proxy>("proxy");

var tunnel = builder.AddDevTunnel("tunnel", tunnelId: tunnelId)
                    .WithReference(proxy);

if (anonymousAccess)
{
    tunnel.WithAnonymousAccess();
}

builder.Build().Run();

static void VerifyDevtunnelTokenCache(string tunnelId)
{
    string output;
    try
    {
        var psi = new ProcessStartInfo("devtunnel", $"show {tunnelId}")
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
