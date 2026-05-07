# Multiple proxies and devtunnels — design

Date: 2026-05-07
Branch: `feat/multi-proxy-support`
Status: Approved for planning

## Goal

Run any number of YARP proxy + devtunnel pairs from a single AppHost. Each pair has its own public URL, its own routes/clusters, its own CORS, its own access mode. Adding a pair is "drop a JSON file and restart". The current single-proxy behaviour is replaced, not layered on top.

## Topology

One Aspire AppHost orchestrates N pairs. Each pair is:

- one `Projects.Proxy` instance on its own port (Aspire-assigned)
- one devtunnel forwarding to that proxy with a stable slug
- a private `IConfiguration` slice loaded from a single JSON file

Pairs share nothing at runtime. Two pairs can pick the same internal cluster id without conflict — they live in separate processes.

## Config layout

```
src/AppHost/proxies/
  example.json     →  Aspire: proxy-example + tunnel-example   (slug "example")
  api.json         →  Aspire: proxy-api     + tunnel-api       (slug "api")
  webhooks.json    →  Aspire: proxy-webhooks + tunnel-webhooks (slug "webhooks")
```

The folder lives inside the AppHost project so it's resolved relative to `builder.AppHostDirectory` with no working-directory ambiguity. A user who wants the folder elsewhere sets `Proxies:Directory` in `src/AppHost/appsettings.json`.

### Filename rules

The filename (without extension) is the single source of truth: it becomes the Aspire resource id (`proxy-{name}`, `tunnel-{name}`) AND the public devtunnel slug. It must match:

```
^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$
```

Lowercase letters, digits, hyphens; 1–32 characters; must start and end alphanumeric. This matches devtunnel's slug constraints. A bad filename fails AppHost startup with a message naming the offending file.

Slug uniqueness is automatic — filenames in one folder can't collide.

### Per-pair file schema

```jsonc
// src/AppHost/proxies/api.json    →  slug "api"
{
  "DevTunnel": {
    "AnonymousAccess": true                // optional, default true
  },
  "Cors": {
    "Policies": {
      "AllowAll": {
        "AllowedOrigins": ["*"],
        "AllowedMethods": ["*"],
        "AllowedHeaders": ["*"],
        "AllowCredentials": false
      }
    }
  },
  "ReverseProxy": {
    "Routes": {
      "default": {
        "ClusterId": "default",
        "CorsPolicy": "AllowAll",
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

- `DevTunnel:AnonymousAccess` is the only key the AppHost reads. Default `true` matches today's behaviour.
- Everything else is the YARP and CORS shape the existing Proxy code already understands. No new schema invented; no .NET config classes needed.
- A JSON parse error surfaces at AppHost startup with the offending filename.

## AppHost behaviour

`src/AppHost/AppHost.cs` replaces the single-tunnel block with a folder scan:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var directory = builder.Configuration["Proxies:Directory"]
    ?? Path.Combine(builder.AppHostDirectory, "proxies");

var files = Directory.Exists(directory)
    ? Directory.GetFiles(directory, "*.json").OrderBy(f => f).ToArray()
    : [];

if (files.Length == 0)
{
    throw new InvalidOperationException(
        $"No proxy configurations found in '{directory}'. " +
        "Add at least one <slug>.json file.");
}

VerifyDevtunnelTokenCache();   // refactored: no slug arg

foreach (var file in files)
{
    var name = Path.GetFileNameWithoutExtension(file);
    if (!ProxySlug.IsValid(name))
    {
        throw new InvalidOperationException(
            $"Proxy config filename '{Path.GetFileName(file)}' is not a valid devtunnel slug " +
            "(lowercase letters, digits, hyphens; 1–32 chars; must start and end alphanumeric).");
    }

    var pairConfig = new ConfigurationBuilder().AddJsonFile(file, optional: false).Build();
    var anonymous = pairConfig.GetValue("DevTunnel:AnonymousAccess", true);

    var proxy = builder.AddProject<Projects.Proxy>($"proxy-{name}")
                       .WithEnvironment("Proxy__ConfigFile", file);

    var tunnel = builder.AddDevTunnel($"tunnel-{name}", tunnelId: name)
                        .WithReference(proxy);

    if (anonymous) tunnel.WithAnonymousAccess();
}

builder.Build().Run();
```

Notes:

- Files iterated in alphabetical order so the resource graph is deterministic across runs.
- `Proxy__ConfigFile` carries the absolute path to that pair's JSON. Aspire injects it as an env var, surfacing in `IConfiguration` as `Proxy:ConfigFile`.
- Empty folder → throw. Silent no-op would hide misconfiguration.
- `ProxySlug.IsValid` and the file-loading helper move to a small static class (`ProxySlug`, `ProxyConfigFile`) so they're unit-testable without spinning up Aspire.

### Pre-flight cache check

The current check runs `devtunnel show <id>` and looks for the corrupt-cache error string. With N pairs there's no single id, but the cache bug is **global per OS** — running the check once is enough. Refactor to call `devtunnel list` (or any tunnel-listing command), which deserialises the same cache and produces the same error when corrupt. Run once at AppHost startup, before the resource loop. Error message and remediation steps unchanged.

## Proxy changes

`src/Proxy/Program.cs` adds three lines:

```csharp
var builder = WebApplication.CreateBuilder(args);

var configFile = builder.Configuration["Proxy:ConfigFile"];
if (!string.IsNullOrEmpty(configFile))
{
    builder.Configuration.AddJsonFile(configFile, optional: false, reloadOnChange: true);
}

builder.Services.AddReverseProxy()
       .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
       .AddTransforms<InjectJsonFieldsTransformProvider>();

builder.Services.AddCors(options =>
    CorsPolicyConfigurator.Configure(options, builder.Configuration.GetSection("Cors:Policies")));
// ...rest unchanged
```

Why this shape:

- One env var, absolute path: no CLI args, no working-directory ambiguity.
- `reloadOnChange: true` keeps YARP's per-route hot-reload promise from the README — edits to a pair's file apply live without restart. (Slug and access mode still need a restart; that's an AppHost-level concern.)
- The `if (!string.IsNullOrEmpty(...))` guard exists only so tests can run `Program` via `WebApplicationFactory<Program>` without an AppHost. Production always has the env var set.
- No new C# config class — `Proxy:ConfigFile` is a single string read once at startup.

## Migration

Concrete edits:

| File | Change |
|---|---|
| `src/AppHost/AppHost.cs` | Rewritten to scan folder + loop, as above. |
| `src/AppHost/appsettings.json` | Drop `DevTunnel` block. Keep `Logging`. Optional `Proxies:Directory` documented but not present by default. |
| `src/AppHost/proxies/example.json` | **New.** Today's quickstart values: anonymous, AllowAll CORS, single catch-all route to `https://example.com/`. Acts as the smoke-test pair on a fresh clone. |
| `src/Proxy/appsettings.json` | Drop `Cors` and `ReverseProxy`. Keep `Logging` and `AllowedHosts`. |
| `src/Proxy/Program.cs` | Three lines added (the `Proxy:ConfigFile` block above). |
| `tests/Proxy.Tests/Integration/ProxyAppFactory.cs` | Add baseline `Cors:Policies:AllowAll` and `ReverseProxy:Routes:default`/`Clusters:default` to the in-memory overrides so existing tests still get a working proxy without `Proxy__ConfigFile`. |
| `README.md` | Quickstart and use-case sections repointed at `src/AppHost/proxies/<slug>.json`. New "Add another proxy" subsection: drop a file, restart, get a second URL. |

No backwards-compatibility shim — the user chose a clean cut. Existing single-proxy users follow the migration in the README on upgrade.

## Testing

### Unit tests (new)

Folder: `tests/Proxy.Tests/AppHost/`. These exist because `IsValidSlug` and `LoadAnonymousAccess` are pulled out of `Program.cs` into `ProxySlug` / `ProxyConfigFile` static classes — pure functions, no I/O at the test seam.

- `ProxySlug.IsValid` accepts `api`, `api-v2`, `a`, `a1b2`, 32-char strings.
- `ProxySlug.IsValid` rejects empty, `Api`, `-api`, `api-`, `api_v2`, 33-char strings.
- `ProxyConfigFile.LoadAnonymousAccess` returns `true` when the key is omitted, `true` when explicitly `true`, `false` when explicitly `false`.

To make this work the AppHost project exposes `InternalsVisibleTo("Proxy.Tests")`, mirroring the Proxy project's setup.

### Integration tests (existing)

Behaviour unchanged. Only `ProxyAppFactory.ConfigureWebHost` changes — its in-memory override dictionary gains the CORS + ReverseProxy baseline that used to come from `src/Proxy/appsettings.json`. Per-test `extraConfig` continues to layer on top.

### Manual verification (run before merge)

1. Fresh clone, `dotnet run --project src/AppHost` → dashboard shows `proxy-example` + `tunnel-example`. Hitting the tunnel URL forwards to `example.com`.
2. Drop `src/AppHost/proxies/api.json` (different slug, different upstream) → restart → both pairs in dashboard, both URLs reachable, each forwarding to its own upstream.
3. Set `"AnonymousAccess": false` on one pair, restart → that tunnel requires sign-in, the other stays public.
4. Edit a route inside one pair's file while running → YARP picks it up live (no restart), the other pair untouched.
5. Bad filename (`Api.json`, `api_v2.json`) → startup fails with a clear message naming the file.
6. Empty `proxies/` folder → startup fails with "No proxy configurations found in '<path>'".

### Out of scope

- Aspire `DistributedApplicationTesting` harness — heavy, low return for this change. Flag as a follow-up if AppHost churn grows.
- Hot add/remove of pairs at runtime — explicitly rejected during brainstorming. Static at AppHost startup; Ctrl+C and re-run to add/remove a pair.

## Risks and follow-ups

- **devtunnel rate limits**: each pair holds its own tunnel. Users with many pairs may hit account-level tunnel limits. Document in README; out of scope to mitigate.
- **Port pressure**: each Proxy instance binds two ports (HTTP + HTTPS via Aspire). Should be fine for double-digit pair counts on a dev machine.
- **Pre-flight check refactor** uses `devtunnel list`. If a future CLI version stops triggering the cache deserialise on `list`, the check silently passes when it should fail. Low likelihood; if it happens we revisit.
- **Transformations stay per-pair**. The `InjectJsonField` transform is configured via per-route metadata inside the pair's file, no sharing across pairs. Matches today's behaviour.
