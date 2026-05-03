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
3. Edit `src/Proxy/appsettings.json` — point `Clusters.default.Destinations.primary.Address` at the URL you want to forward to. Save; YARP hot-reloads, no restart.
4. Edit `src/AppHost/appsettings.json` — change `DevTunnel:Id` to your own globally-unique slug (a-z, 0-9, hyphen) so collaborators get their own stable URL.

Optional features: [CORS](#cors), [request header injection](#header-injection), [JSON body field injection](#json-body-field-injection), [private tunnel](#security).

YARP route/cluster reference: <https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/config-files>.

### CORS

Declare named CORS policies under `Cors:Policies`, then reference them from any route via the standard YARP `CorsPolicy` field. The bundled default policy is wide-open:

```json
"Cors": {
  "Policies": {
    "default": {
      "AllowedOrigins": ["*"],
      "AllowedMethods": ["*"],
      "AllowedHeaders": ["*"],
      "ExposedHeaders": [],
      "AllowCredentials": false,
      "PreflightMaxAgeSeconds": null
    }
  }
}
```

- `["*"]` for `AllowedOrigins`, `AllowedMethods`, or `AllowedHeaders` enables the matching `AllowAny*` rule. An explicit list narrows the policy to those values.
- `AllowCredentials: true` combined with `AllowedOrigins: ["*"]` is invalid — startup throws `InvalidOperationException` with the policy name. List explicit origins instead.
- `PreflightMaxAgeSeconds` (optional) sets the `Access-Control-Max-Age` response header for preflight caching.
- Routes opt in by setting `CorsPolicy: "<name>"`. ASP.NET Core CORS middleware short-circuits `OPTIONS` preflights so YARP never forwards them upstream.
- CORS policies are read at startup. Editing `Cors:Policies` requires an AppHost restart. `Routes` and `Clusters` continue to hot-reload as before.

Reference: <https://learn.microsoft.com/aspnet/core/security/cors>.

### Header injection

Add a `Transforms` array inside a route to set or append request headers before forwarding:

```json
"Routes": {
  "default": {
    "ClusterId": "default",
    "CorsPolicy": "default",
    "Match": { "Path": "{**catch-all}" },
    "Transforms": [
      { "RequestHeader": "X-Server-Secret", "Set": "your-secret-here" },
      { "RequestHeader": "X-Trace", "Append": "proxy" }
    ]
  }
}
```

- `Set` overwrites a client-supplied value of the same name.
- `Append` adds a value without replacing existing ones (multiple values become comma-joined).
- Client-supplied headers pass through by default.

Reference: <https://microsoft.github.io/reverse-proxy/articles/transforms-request.html>.

### JSON body field injection

For upstreams that authenticate via JSON body fields rather than headers, declare the fields to merge under the route's `Metadata` block with the `InjectJsonField:` prefix:

```json
"Routes": {
  "default": {
    "ClusterId": "default",
    "CorsPolicy": "default",
    "Match": { "Path": "{**catch-all}" },
    "Metadata": {
      "InjectJsonField:AuthToken": "your-server-side-secret",
      "InjectJsonField:Count": "42",
      "InjectJsonField:Nested": "{\"k\":\"v\"}"
    }
  }
}
```

- Fields are merged into the top level of the JSON request body before forwarding.
- Each value is parsed as JSON first: `"true"` becomes a boolean, `"42"` becomes a number, `"{\"k\":\"v\"}"` becomes a nested object. Anything that isn't valid JSON is injected as a raw string.
- If a client-supplied field collides with an injected key, the injected value wins.
- Skipped silently when the request `Content-Type` is not `application/json`, the body is empty, or the parsed JSON isn't an object — the body passes through unchanged so the upstream's own validation surfaces the error.

## Security

`DevTunnel:AnonymousAccess: true` (the default) makes the tunnel URL **publicly reachable by anyone who knows it**. Don't proxy anything with secrets, dev databases, or unauthenticated admin surfaces.

To make the tunnel private, set `DevTunnel:AnonymousAccess` to `false` in `src/AppHost/appsettings.json`. Recipients then need a Microsoft or GitHub login the tunnel owner has authorised, or an `X-Tunnel-Authorization` token issued by `devtunnel token`. Note: a private tunnel breaks cross-origin browser callers — `fetch()` from a deployed SPA on another origin cannot complete the interactive sign-in flow.

## Layout

```
src/
├── AppHost/   .NET Aspire app host: wires the proxy to a Dev Tunnel
└── Proxy/     ASP.NET Core + YARP: routes/clusters live in appsettings.json
```

## License

MIT — see [LICENSE](LICENSE).
