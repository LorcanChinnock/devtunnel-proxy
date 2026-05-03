# aspire-tunnel-proxy

A tiny, config-driven local reverse proxy with a stable public HTTPS URL. Edit a JSON file to declare what to forward where, run one command, share the link.

Built on [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) + [YARP](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/) + [Microsoft Dev Tunnels](https://learn.microsoft.com/azure/developer/dev-tunnels/).

## Prerequisites

- **.NET 10 SDK** — <https://dotnet.microsoft.com/download/dotnet/10.0>
- **`devtunnel` CLI** — install per OS:
  - macOS: `brew install --cask devtunnel`
  - Windows: `winget install Microsoft.devtunnel`
  - Linux: `curl -sL https://aka.ms/DevTunnelCliInstall | bash`
  - More: <https://learn.microsoft.com/azure/developer/dev-tunnels/get-started#install>
- A Microsoft or GitHub account (the `devtunnel` CLI prompts you to sign in on first run; Aspire handles this automatically).

## Run it

```bash
git clone <repo-url>
cd aspire-tunnel-proxy
dotnet run --project src/AppHost
```

Works identically on macOS, Linux, and Windows (PowerShell or `cmd`). On first run, a browser window opens for `devtunnel` sign-in.

## Use it

1. Open the Aspire dashboard URL printed in the terminal.
2. Click the `tunnel` resource and copy its `https://aspire-tunnel-proxy-*.devtunnels.ms` URL.
3. Edit `src/Proxy/appsettings.json` — change `Clusters.default.Destinations.primary.Address` to the local or remote URL you want to forward to. Save; YARP hot-reloads, no restart.
4. Edit `src/AppHost/appsettings.json` — change `DevTunnel:Id` to your own globally-unique slug (a-z, 0-9, hyphen) so collaborators get their own stable URL.

YARP route/cluster reference: <https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/config-files>.

## Security

`.WithAnonymousAccess()` makes the tunnel URL **publicly reachable by anyone who knows it**. Do not proxy to anything with secrets, dev databases, or unauthenticated admin surfaces.

To make the tunnel private, delete the `.WithAnonymousAccess()` line in `src/AppHost/AppHost.cs`. Recipients will then need a Microsoft or GitHub login that the tunnel owner has authorised.

## Layout

```
src/
├── AppHost/   .NET Aspire app host: wires the proxy to a Dev Tunnel
└── Proxy/     ASP.NET Core + YARP: routes/clusters live in appsettings.json
```

## License

MIT — see [LICENSE](LICENSE).
