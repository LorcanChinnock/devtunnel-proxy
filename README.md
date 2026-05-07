# devtunnel-proxy

[![CI](https://github.com/LorcanChinnock/devtunnel-proxy/actions/workflows/ci.yml/badge.svg)](https://github.com/LorcanChinnock/devtunnel-proxy/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)

A config-driven local reverse proxy with a stable public HTTPS URL. Edit one JSON file, run one command, share the link.

Built on [.NET Aspire](https://github.com/microsoft/aspire), [YARP](https://github.com/dotnet/yarp), and [Microsoft Dev Tunnels](https://github.com/microsoft/dev-tunnels).

## Why this exists

You need a public HTTPS URL that points at something running on your laptop — to receive a webhook, demo a prototype, or test a mobile app against a local API. ngrok works, but you pay for stable URLs and lose control over headers, CORS, and request shaping. This project is the same idea, free, and you keep full control: add CORS, inject auth headers or JSON fields, hot-reload routes from a config file.

## Quickstart

```bash
git clone https://github.com/LorcanChinnock/devtunnel-proxy.git
cd devtunnel-proxy
dotnet run --project src/AppHost
```

First run opens a browser for `devtunnel` sign-in. Then:

1. Open the Aspire dashboard URL printed in the terminal.
2. Click the `tunnel-example` resource — copy its `https://*.devtunnels.ms` URL.
3. Edit `src/AppHost/proxies/example.json` → set `ReverseProxy.Clusters.default.Destinations.primary.Address` to your upstream. YARP hot-reloads — no restart needed.
4. To publish under a different stable URL, rename the file (e.g. `mv example.json my-slug.json`) and restart. The filename is the slug.

That's it. Anything hitting the tunnel URL is forwarded to your configured destination.

### Add another proxy

Drop a second JSON file in `src/AppHost/proxies/`:

```bash
cp src/AppHost/proxies/example.json src/AppHost/proxies/api.json
# edit api.json — point Address at a different upstream
dotnet run --project src/AppHost
```

The dashboard now shows two pairs: `proxy-example` + `tunnel-example` and `proxy-api` + `tunnel-api`, each with its own public URL. Pairs are static — Ctrl+C and re-run to add or remove one.

Filename rules: lowercase letters, digits, and hyphens only; 1–32 characters; must start and end alphanumeric. Bad filenames fail AppHost startup with a clear message.

### Requirements

| | Version |
|---|---|
| .NET SDK | 10.0+ |
| `devtunnel` CLI | latest ([install](https://learn.microsoft.com/azure/developer/dev-tunnels/get-started#install)) |
| Account | Microsoft or GitHub (for `devtunnel` sign-in) |

`devtunnel` install one-liners — macOS: `brew install --cask devtunnel`; Windows: `winget install Microsoft.devtunnel`; Linux: `curl -sL https://aka.ms/DevTunnelCliInstall | bash`.

## Use cases

### Receive webhooks from third-party services

Stripe, GitHub, Slack, etc. need a public HTTPS endpoint to send events. Point the proxy at your local handler, paste the tunnel URL into the provider's webhook config.

```jsonc
// src/AppHost/proxies/webhooks.json
{
  "DevTunnel": { "AnonymousAccess": true },
  "ReverseProxy": {
    "Routes": {
      "default": {
        "ClusterId": "default",
        "Match": { "Path": "{**catch-all}" }
      }
    },
    "Clusters": {
      "default": {
        "Destinations": {
          "primary": { "Address": "http://localhost:5050/" }
        }
      }
    }
  }
}
```

Now `https://<your-slug>.devtunnels.ms/stripe/events` reaches `http://localhost:5050/stripe/events`.

### Test a local API from a real mobile device

Phones, tablets, and other dev machines can't reach `localhost:5000`. Run the proxy, give them the tunnel URL — works over cellular, hotel Wi-Fi, anywhere.

### Demo a prototype to a teammate

```bash
dotnet run --project src/AppHost
# share the printed devtunnels.ms URL
```

Stable across restarts as long as the proxy filename doesn't change.

### Add CORS to an upstream that doesn't support it

Wrap a bare API with browser-friendly CORS without modifying the upstream:

```jsonc
// src/AppHost/proxies/cors-bridge.json
{
  "DevTunnel": { "AnonymousAccess": true },
  "Cors": {
    "Policies": {
      "default": {
        "AllowedOrigins": ["https://my-spa.example.com"],
        "AllowedMethods": ["GET", "POST"],
        "AllowedHeaders": ["*"],
        "AllowCredentials": false
      }
    }
  },
  "ReverseProxy": {
    "Routes": {
      "default": {
        "ClusterId": "default",
        "CorsPolicy": "default",
        "Match": { "Path": "{**catch-all}" }
      }
    },
    "Clusters": {
      "default": {
        "Destinations": {
          "primary": { "Address": "http://localhost:5050/" }
        }
      }
    }
  }
}
```

`["*"]` enables `AllowAny*`. Combining `AllowedOrigins: ["*"]` with `AllowCredentials: true` is invalid and throws at startup — list explicit origins instead.

### Inject auth headers before forwarding

Public-facing tunnel, secret-bearing upstream. Use route transforms:

```jsonc
// src/AppHost/proxies/auth-injection.json
{
  "DevTunnel": { "AnonymousAccess": true },
  "ReverseProxy": {
    "Routes": {
      "default": {
        "ClusterId": "default",
        "Match": { "Path": "{**catch-all}" },
        "Transforms": [
          { "RequestHeader": "X-Api-Key", "Set": "your-secret-here" },
          { "RequestHeader": "X-Trace", "Append": "proxy" }
        ]
      }
    },
    "Clusters": {
      "default": {
        "Destinations": {
          "primary": { "Address": "http://localhost:5050/" }
        }
      }
    }
  }
}
```

`Set` overwrites client-supplied values. `Append` adds without replacing.

### Inject auth fields into JSON request bodies

For upstreams that authenticate via body fields, not headers:

```jsonc
// src/AppHost/proxies/body-auth.json
{
  "DevTunnel": { "AnonymousAccess": true },
  "ReverseProxy": {
    "Routes": {
      "default": {
        "ClusterId": "default",
        "Match": { "Path": "{**catch-all}" },
        "Metadata": {
          "InjectJsonField.AuthToken": "your-server-side-secret",
          "InjectJsonField.Count": "42",
          "InjectJsonField.Nested": "{\"k\":\"v\"}"
        }
      }
    },
    "Clusters": {
      "default": {
        "Destinations": {
          "primary": { "Address": "http://localhost:5050/" }
        }
      }
    }
  }
}
```

Each value parses as JSON first — `"42"` becomes a number, `"true"` a boolean, `"{...}"` a nested object. Anything else is injected as a string. Skipped silently when the body isn't JSON, is empty, or isn't an object.

## Configuration reference

| File | Purpose |
|---|---|
| `src/AppHost/proxies/<slug>.json` | One file per proxy: tunnel access mode, YARP routes/clusters, CORS policies |
| `src/AppHost/appsettings.json` | Logging only. Optional `Proxies:Directory` to relocate the proxies folder |
| `src/Proxy/appsettings.json` | Logging only — runtime YARP/CORS config comes from the per-pair file via `Proxy__ConfigFile` |

YARP routes/clusters fully follow the upstream schema — see [YARP config files](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/config-files).

**Hot reload:** `Routes` and `Clusters` reload on save with no restart. `Cors:Policies` are read at startup — changes need an AppHost restart.

## Security

`DevTunnel:AnonymousAccess: true` (the default) makes the tunnel URL **publicly reachable by anyone who knows it**. Don't proxy anything with secrets, dev databases, or unauthenticated admin surfaces over an anonymous tunnel.

Set `DevTunnel:AnonymousAccess: false` in the per-pair file in `src/AppHost/proxies/` for a private tunnel. Recipients then need a Microsoft/GitHub login the owner has authorised, or an `X-Tunnel-Authorization` token from `devtunnel token`. Note: private tunnels block cross-origin browser callers — `fetch()` from a deployed SPA on another origin can't complete the interactive sign-in.

To report a vulnerability privately, please open a [GitHub security advisory](https://github.com/LorcanChinnock/devtunnel-proxy/security/advisories/new) rather than a public issue.

## Troubleshooting

### `An item with the same key has already been added. Key: host`

Aspire startup fails with `Failed to create dev tunnel '<id>'` and the inner error is `An item with the same key has already been added. Key: host`. The same string also appears if you run `devtunnel show <id>` directly.

This is a bug in the `devtunnel` CLI's tunnel-access-token cache: two cached entries collide on the scope key `host`, and every partial-ID lookup throws while deserializing the cache. Logging out (`devtunnel user logout`) does **not** clear it — the data lives in the OS credential store under a separate entry.

The AppHost runs a startup pre-flight check that detects this and throws with the same fix instructions. To clear the cache:

| OS | Command |
|---|---|
| macOS | `security delete-generic-password -s tunnels -a "https://global.rel.tunnels.api.visualstudio.com/auth/tunnels"` |
| Linux | Remove the libsecret item with label `tunnels` and account `…/auth/tunnels` (e.g. via `secret-tool clear` or Seahorse). |
| Windows | Open Credential Manager → Windows Credentials → remove the `tunnels` entry whose target is `…/auth/tunnels`. |

After clearing, re-run — the CLI repopulates the cache cleanly. Your user login (the `auth/github` or `auth/microsoft` entry) is untouched.

## Project layout

```
src/
├── AppHost/
│   ├── proxies/   one .json file per public URL — drop in to add a pair
│   └── ...        .NET Aspire app host wiring proxies to dev tunnels
└── Proxy/         ASP.NET Core + YARP — reads its slice via Proxy:ConfigFile
tests/
└── Proxy.Tests/   xUnit v3 integration + unit tests
```

## Contributing

Issues and pull requests welcome. PRs run a build + test gate via [GitHub Actions](.github/workflows/ci.yml) and require one CODEOWNER review before merge.

## License

[MIT](LICENSE).
