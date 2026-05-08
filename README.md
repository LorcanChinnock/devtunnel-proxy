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
3. Edit `src/Proxy/appsettings.json` → set `Proxies.example.ReverseProxy.Clusters.default.Destinations.primary.Address` to your upstream. YARP hot-reloads — no restart needed.
4. To publish under a different stable URL, rename the slug key (e.g. rename `"example"` to `"my-slug"`) and restart. The key is the slug.

That's it. Anything hitting the tunnel URL is forwarded to your configured destination.

### Add another proxy

Personal proxies belong in `src/Proxy/appsettings.Development.json` — the standard ASP.NET overlay, gitignored so secrets and per-developer routes never end up in commits. Defaults that should ship live in `src/Proxy/appsettings.json`.

```jsonc
// src/Proxy/appsettings.Development.json — gitignored, your local overlay
{
  "Proxies": {
    "api": {
      "DevTunnel": { "AnonymousAccess": true },
      "ReverseProxy": {
        "Routes":   { "default": { "ClusterId": "default", "Match": { "Path": "{**catch-all}" } } },
        "Clusters": { "default": { "Destinations": { "primary": { "Address": "http://localhost:5050/" } } } }
      }
    }
  }
}
```

Restart and the dashboard now shows two pairs: `proxy-example` + `tunnel-example` and `proxy-api` + `tunnel-api`, each with its own public URL. Pairs are static — Ctrl+C and re-run to add or remove one.

Slug rules: lowercase letters, digits, and hyphens only; 1–32 characters; must start and end alphanumeric. Bad slugs fail AppHost startup with a clear message.

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
// src/Proxy/appsettings.Development.json
{
  "Proxies": {
    "webhooks": {
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
// src/Proxy/appsettings.Development.json
{
  "Proxies": {
    "cors-bridge": {
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
  }
}
```

`["*"]` enables `AllowAny*`. Combining `AllowedOrigins: ["*"]` with `AllowCredentials: true` is invalid and throws at startup — list explicit origins instead.

### Inject auth headers before forwarding

Public-facing tunnel, secret-bearing upstream. Use route transforms:

```jsonc
// src/Proxy/appsettings.Development.json
{
  "Proxies": {
    "auth-injection": {
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
  }
}
```

`Set` overwrites client-supplied values. `Append` adds without replacing.

### Inject auth fields into JSON request bodies

For upstreams that authenticate via body fields, not headers:

```jsonc
// src/Proxy/appsettings.Development.json
{
  "Proxies": {
    "body-auth": {
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
  }
}
```

Each value parses as JSON first — `"42"` becomes a number, `"true"` a boolean, `"{...}"` a nested object. Anything else is injected as a string. Skipped silently when the body isn't JSON, is empty, or isn't an object.

## Configuration reference

All proxies live under the `Proxies:<slug>:*` section of the standard ASP.NET appsettings files in `src/Proxy/`:

| File | Purpose |
|---|---|
| `src/Proxy/appsettings.json` | Committed defaults. Logging plus any baseline `Proxies:<slug>:*` entries that should ship with the repo |
| `src/Proxy/appsettings.Development.json` | Personal overlay (gitignored). Add or override your own `Proxies:<slug>:*` entries here — secrets, local upstreams, work-in-progress routes |
| `src/AppHost/appsettings.json` | Logging only |

The AppHost reads `src/Proxy/appsettings.json` plus `src/Proxy/appsettings.{Environment}.json` to enumerate slugs and their `DevTunnel:AnonymousAccess` flag, then spawns one `proxy-<slug>` + `tunnel-<slug>` pair per entry, passing each Proxy instance its own `Proxy__Slug` env var so it binds only the matching slice.

YARP routes/clusters fully follow the upstream schema — see [YARP config files](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/config-files).

**Hot reload:** `Routes` and `Clusters` reload on save with no restart. `Cors:Policies` are read at startup — changes need an AppHost restart. Adding or removing a slug also requires a restart.

### Stable tunnel URLs (`Port`)

Devtunnel URLs are `https://<routing-id>-<port>.<region>.devtunnels.ms`. The `<routing-id>` is stable for the tunnel ID, but the `<port>` is the proxy's external endpoint port — and by default the AppHost lets Aspire pick a free port at startup, so the URL **changes every time the AppHost restarts**. That's fine for one-off testing but breaks every webhook/SPA/mobile-device caller that has the URL.

Set `Proxies:<slug>:Port` to pin the external endpoint to a fixed port. The tunnel forwards to that port, so the URL stays the same across restarts:

```jsonc
"connectorapidev": {
  "Port": 62844,            // pin -> https://<id>-62844.<region>.devtunnels.ms is permanent
  "DevTunnel": { "AnonymousAccess": true },
  "ReverseProxy": { ... }
}
```

Pick any free port; the only constraint is no two slugs (or any other local service) can share one. Omit `Port` for dynamic assignment when URL stability doesn't matter.

## Security

`DevTunnel:AnonymousAccess: true` (the default) makes the tunnel URL **publicly reachable by anyone who knows it**. Don't proxy anything with secrets, dev databases, or unauthenticated admin surfaces over an anonymous tunnel.

Set `Proxies:<slug>:DevTunnel:AnonymousAccess: false` in your appsettings for a private tunnel. Recipients then need a Microsoft/GitHub login the owner has authorised, or an `X-Tunnel-Authorization` token from `devtunnel token`. Note: private tunnels block cross-origin browser callers — `fetch()` from a deployed SPA on another origin can't complete the interactive sign-in.

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
├── AppHost/                       .NET Aspire app host — enumerates slugs from Proxy/appsettings.* and wires each to a dev tunnel
└── Proxy/
    ├── appsettings.json           committed defaults: Logging + baseline Proxies:<slug>:* entries
    ├── appsettings.Development.json  gitignored personal overlay (your Proxies:<slug>:* entries)
    └── ...                        ASP.NET Core + YARP — reads its slice via Proxy:Slug
tests/
└── Proxy.Tests/   xUnit v3 integration + unit tests
```

## Contributing

Issues and pull requests welcome. PRs run a build + test gate via [GitHub Actions](.github/workflows/ci.yml) and require one CODEOWNER review before merge.

## License

[MIT](LICENSE).
