# Multiple proxies and devtunnels — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the AppHost's single hard-coded `DevTunnel:Id` with a folder scan that creates one `Projects.Proxy` instance + one devtunnel per JSON file in `src/AppHost/proxies/`.

**Architecture:** AppHost discovers `*.json` files at startup; filename = Aspire resource name = devtunnel slug. Each Proxy instance reads its assigned file via the `Proxy__ConfigFile` env var injected by Aspire. YARP's per-route hot-reload still works (per pair). Static at AppHost startup — adding/removing a pair requires restart.

**Tech Stack:** .NET 10, Aspire 13.2 (Aspire.Hosting.DevTunnels), YARP 2.3, xUnit v3, Microsoft.AspNetCore.Mvc.Testing.

**Spec:** `docs/superpowers/specs/2026-05-07-multiple-proxies-design.md`

**Branch:** `feat/multi-proxy-support`

---

## File map

**Create:**
- `src/AppHost/ProxySlug.cs` — `public static partial class` with regex-based `IsValid(string?)` helper.
- `src/AppHost/ProxyConfigFile.cs` — `public static class` with `LoadAnonymousAccess(string)` helper.
- `src/AppHost/proxies/example.json` — first config-driven pair, replicates today's quickstart.
- `tests/Proxy.Tests/Hosting/ProxySlugTests.cs` — xUnit tests for slug validation.
- `tests/Proxy.Tests/Hosting/ProxyConfigFileTests.cs` — xUnit tests for the config helper.

**Modify:**
- `src/AppHost/AppHost.cs` — replace single-tunnel block with folder scan + loop; refactor pre-flight check.
- `src/AppHost/appsettings.json` — drop `DevTunnel` block.
- `src/Proxy/Program.cs` — add three lines to load `Proxy:ConfigFile`.
- `src/Proxy/appsettings.json` — drop `Cors` and `ReverseProxy` sections.
- `tests/Proxy.Tests/Integration/ProxyAppFactory.cs` — add baseline CORS + ReverseProxy in-memory overrides.
- `tests/Proxy.Tests/Proxy.Tests.csproj` — add `ProjectReference` to `AppHost.csproj`.
- `README.md` — quickstart and use-case sections repointed at per-pair files; add "Add another proxy" subsection.

---

## Task 1: ProxySlug helper (TDD)

**Files:**
- Create: `src/AppHost/ProxySlug.cs`
- Create: `tests/Proxy.Tests/Hosting/ProxySlugTests.cs`
- Modify: `tests/Proxy.Tests/Proxy.Tests.csproj`

- [ ] **Step 1: Add ProjectReference to AppHost in the test csproj**

Replace the existing single-line `<ItemGroup>` containing `Proxy.csproj` reference (around line 22 of `tests/Proxy.Tests/Proxy.Tests.csproj`) with:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\Proxy\Proxy.csproj" />
    <ProjectReference Include="..\..\src\AppHost\AppHost.csproj" />
  </ItemGroup>
```

- [ ] **Step 2: Verify the test project still builds**

```bash
timeout 60 dotnet build tests/Proxy.Tests/Proxy.Tests.csproj
```

Expected: Build succeeds. (If it fails because Aspire SDK objects to being referenced from a non-Aspire project, fall back to extracting helpers into a new `src/Hosting/Hosting.csproj` class library and reference that from both AppHost and Proxy.Tests — adjust subsequent tasks accordingly.)

- [ ] **Step 3: Write the failing test**

Create `tests/Proxy.Tests/Hosting/ProxySlugTests.cs`:

```csharp
using AppHost;

namespace Proxy.Tests.Hosting;

public class ProxySlugTests
{
    [Theory]
    [InlineData("api")]
    [InlineData("a")]
    [InlineData("a1")]
    [InlineData("api-v2")]
    [InlineData("1abc")]
    [InlineData("ab")]
    public void IsValid_returns_true_for_valid_slug(string name) =>
        Assert.True(ProxySlug.IsValid(name));

    [Fact]
    public void IsValid_returns_true_for_max_length_slug() =>
        Assert.True(ProxySlug.IsValid("a" + new string('b', 30) + "c"));

    [Theory]
    [InlineData("")]
    [InlineData("Api")]
    [InlineData("-api")]
    [InlineData("api-")]
    [InlineData("api_v2")]
    [InlineData(" api ")]
    public void IsValid_returns_false_for_invalid_slug(string name) =>
        Assert.False(ProxySlug.IsValid(name));

    [Fact]
    public void IsValid_returns_false_for_too_long_slug() =>
        Assert.False(ProxySlug.IsValid(new string('a', 33)));

    [Fact]
    public void IsValid_returns_false_for_null() =>
        Assert.False(ProxySlug.IsValid(null));
}
```

- [ ] **Step 4: Run test to verify it fails**

```bash
timeout 120 dotnet test tests/Proxy.Tests/Proxy.Tests.csproj --filter "FullyQualifiedName~ProxySlugTests"
```

Expected: Build fails with "The type or namespace name 'ProxySlug' could not be found" (or "AppHost" namespace not found).

- [ ] **Step 5: Write the implementation**

Create `src/AppHost/ProxySlug.cs`:

```csharp
using System.Text.RegularExpressions;

namespace AppHost;

public static partial class ProxySlug
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);
}
```

- [ ] **Step 6: Run test to verify it passes**

```bash
timeout 120 dotnet test tests/Proxy.Tests/Proxy.Tests.csproj --filter "FullyQualifiedName~ProxySlugTests"
```

Expected: All `ProxySlugTests` pass (15 cases across the theories + facts).

- [ ] **Step 7: Commit**

```bash
git add src/AppHost/ProxySlug.cs tests/Proxy.Tests/Hosting/ProxySlugTests.cs tests/Proxy.Tests/Proxy.Tests.csproj
git commit -m "feat(apphost): add ProxySlug.IsValid helper for filename validation"
```

---

## Task 2: ProxyConfigFile.LoadAnonymousAccess helper (TDD)

**Files:**
- Create: `src/AppHost/ProxyConfigFile.cs`
- Create: `tests/Proxy.Tests/Hosting/ProxyConfigFileTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Proxy.Tests/Hosting/ProxyConfigFileTests.cs`:

```csharp
using AppHost;

namespace Proxy.Tests.Hosting;

public class ProxyConfigFileTests : IDisposable
{
    private readonly string _tempFile = Path.GetTempFileName();

    public void Dispose() => File.Delete(_tempFile);

    [Fact]
    public void LoadAnonymousAccess_returns_true_when_key_omitted()
    {
        File.WriteAllText(_tempFile, "{ \"DevTunnel\": {} }");
        Assert.True(ProxyConfigFile.LoadAnonymousAccess(_tempFile));
    }

    [Fact]
    public void LoadAnonymousAccess_returns_true_when_explicit_true()
    {
        File.WriteAllText(_tempFile, "{ \"DevTunnel\": { \"AnonymousAccess\": true } }");
        Assert.True(ProxyConfigFile.LoadAnonymousAccess(_tempFile));
    }

    [Fact]
    public void LoadAnonymousAccess_returns_false_when_explicit_false()
    {
        File.WriteAllText(_tempFile, "{ \"DevTunnel\": { \"AnonymousAccess\": false } }");
        Assert.False(ProxyConfigFile.LoadAnonymousAccess(_tempFile));
    }

    [Fact]
    public void LoadAnonymousAccess_returns_true_when_devtunnel_section_absent()
    {
        File.WriteAllText(_tempFile, "{ }");
        Assert.True(ProxyConfigFile.LoadAnonymousAccess(_tempFile));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
timeout 120 dotnet test tests/Proxy.Tests/Proxy.Tests.csproj --filter "FullyQualifiedName~ProxyConfigFileTests"
```

Expected: Build fails with "The type or namespace name 'ProxyConfigFile' could not be found".

- [ ] **Step 3: Write the implementation**

Create `src/AppHost/ProxyConfigFile.cs`:

```csharp
using Microsoft.Extensions.Configuration;

namespace AppHost;

public static class ProxyConfigFile
{
    public static bool LoadAnonymousAccess(string filePath)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile(filePath, optional: false)
            .Build();
        return config.GetValue("DevTunnel:AnonymousAccess", true);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
timeout 120 dotnet test tests/Proxy.Tests/Proxy.Tests.csproj --filter "FullyQualifiedName~ProxyConfigFileTests"
```

Expected: All four `ProxyConfigFileTests` pass.

- [ ] **Step 5: Commit**

```bash
git add src/AppHost/ProxyConfigFile.cs tests/Proxy.Tests/Hosting/ProxyConfigFileTests.cs
git commit -m "feat(apphost): add ProxyConfigFile.LoadAnonymousAccess helper"
```

---

## Task 3: Move Proxy test baseline into ProxyAppFactory

The Proxy's `appsettings.json` will lose its `Cors` and `ReverseProxy` sections in the next task. Tests still need that baseline to spin up a working proxy without `Proxy__ConfigFile`. Add it to `ProxyAppFactory`'s in-memory overrides first so the next change doesn't break tests.

**Files:**
- Modify: `tests/Proxy.Tests/Integration/ProxyAppFactory.cs`

- [ ] **Step 1: Add the baseline to the in-memory overrides**

Replace the contents of `tests/Proxy.Tests/Integration/ProxyAppFactory.cs` with:

```csharp
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
                ["Cors:Policies:AllowAll:AllowedOrigins:0"] = "*",
                ["Cors:Policies:AllowAll:AllowedMethods:0"] = "*",
                ["Cors:Policies:AllowAll:AllowedHeaders:0"] = "*",
                ["Cors:Policies:AllowAll:AllowCredentials"] = "false",
                ["ReverseProxy:Routes:default:ClusterId"] = "default",
                ["ReverseProxy:Routes:default:CorsPolicy"] = "AllowAll",
                ["ReverseProxy:Routes:default:Match:Path"] = "{**catch-all}",
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
```

- [ ] **Step 2: Run all existing tests to confirm no behaviour change**

```bash
timeout 180 dotnet test tests/Proxy.Tests/Proxy.Tests.csproj
```

Expected: All tests pass. The in-memory keys override-but-match what `appsettings.json` provides, so test behaviour is unchanged.

- [ ] **Step 3: Commit**

```bash
git add tests/Proxy.Tests/Integration/ProxyAppFactory.cs
git commit -m "refactor(tests): move proxy baseline into ProxyAppFactory"
```

---

## Task 4: Strip Proxy/appsettings.json down to Logging + AllowedHosts

**Files:**
- Modify: `src/Proxy/appsettings.json`

- [ ] **Step 1: Replace the file contents**

Replace the entire contents of `src/Proxy/appsettings.json` with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 2: Run all tests**

```bash
timeout 180 dotnet test tests/Proxy.Tests/Proxy.Tests.csproj
```

Expected: All tests pass. Tests now rely entirely on the in-memory baseline added in Task 3.

- [ ] **Step 3: Commit**

```bash
git add src/Proxy/appsettings.json
git commit -m "refactor(proxy): drop Cors and ReverseProxy from appsettings.json"
```

---

## Task 5: Add Proxy:ConfigFile loader to Program.cs

**Files:**
- Modify: `src/Proxy/Program.cs`

- [ ] **Step 1: Edit Program.cs**

Replace the entire contents of `src/Proxy/Program.cs` with:

```csharp
using Proxy.Cors;
using Proxy.Transforms;

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

var app = builder.Build();

app.UseRouting();
app.UseCors();
app.MapReverseProxy();
app.Run();

public partial class Program;
```

- [ ] **Step 2: Run all tests**

```bash
timeout 180 dotnet test tests/Proxy.Tests/Proxy.Tests.csproj
```

Expected: All tests pass. Tests don't set `Proxy__ConfigFile`, so the new branch is skipped.

- [ ] **Step 3: Commit**

```bash
git add src/Proxy/Program.cs
git commit -m "feat(proxy): load YARP/CORS from Proxy:ConfigFile when set"
```

---

## Task 6: Create example.json

**Files:**
- Create: `src/AppHost/proxies/example.json`

- [ ] **Step 1: Create the proxies folder and example file**

Create `src/AppHost/proxies/example.json` with:

```json
{
  "DevTunnel": {
    "AnonymousAccess": true
  },
  "Cors": {
    "Policies": {
      "AllowAll": {
        "AllowedOrigins": ["*"],
        "AllowedMethods": ["*"],
        "AllowedHeaders": ["*"],
        "ExposedHeaders": [],
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
          "primary": { "Address": "https://example.com/" }
        }
      }
    }
  }
}
```

- [ ] **Step 2: Verify the AppHost project still builds**

```bash
timeout 60 dotnet build src/AppHost/AppHost.csproj
```

Expected: Build succeeds. (No code changes yet, just a new JSON file under the project.)

- [ ] **Step 3: Commit**

```bash
git add src/AppHost/proxies/example.json
git commit -m "feat(apphost): add example proxy config (slug \"example\")"
```

---

## Task 7: Rewrite AppHost.cs and drop DevTunnel from AppHost/appsettings.json

This is the biggest single change. After this task, `dotnet run --project src/AppHost` discovers `proxies/*.json` and spins up one Proxy + tunnel per file.

**Files:**
- Modify: `src/AppHost/AppHost.cs`
- Modify: `src/AppHost/appsettings.json`

- [ ] **Step 1: Replace AppHost.cs contents**

Replace the entire contents of `src/AppHost/AppHost.cs` with:

```csharp
using System.Diagnostics;
using AppHost;
using Microsoft.Extensions.Configuration;

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
                       .WithEnvironment("Proxy__ConfigFile", file);

    var tunnel = builder.AddDevTunnel($"tunnel-{name}", tunnelId: name)
                        .WithReference(proxy);

    if (anonymous)
    {
        tunnel.WithAnonymousAccess();
    }
}

builder.Build().Run();

static void VerifyDevtunnelTokenCache()
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
```

- [ ] **Step 2: Replace AppHost/appsettings.json contents**

Replace the entire contents of `src/AppHost/appsettings.json` with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Aspire.Hosting.Dcp": "Warning"
    }
  }
}
```

- [ ] **Step 3: Build the AppHost**

```bash
timeout 60 dotnet build src/AppHost/AppHost.csproj
```

Expected: Build succeeds with no warnings about unused usings.

- [ ] **Step 4: Run the full test suite**

```bash
timeout 180 dotnet test tests/Proxy.Tests/Proxy.Tests.csproj
```

Expected: All tests pass. AppHost changes don't affect Proxy unit/integration tests.

- [ ] **Step 5: Commit**

```bash
git add src/AppHost/AppHost.cs src/AppHost/appsettings.json
git commit -m "feat(apphost): scan proxies/ folder to spin up one proxy+tunnel per file"
```

---

## Task 8: Manual smoke verification

Aspire orchestration and devtunnel creation aren't covered by automated tests; run this checklist before merging. Each item is verified by observation, not by a passing assertion.

> **Important:** If the run fails with a `devtunnel` cache error, follow the README's Troubleshooting steps to clear the OS credential cache, then retry — this is a pre-existing CLI bug unrelated to this change.

- [ ] **Step 1: Fresh run with the shipped example pair**

```bash
timeout 60 dotnet run --project src/AppHost
```

Expected: Aspire dashboard URL is printed; dashboard shows `proxy-example` (running, healthy) and `tunnel-example` (running, with a `https://*.devtunnels.ms` URL). Hitting that URL forwards to `https://example.com/`. Stop with Ctrl+C.

- [ ] **Step 2: Add a second pair and verify both run**

Create `src/AppHost/proxies/echo.json` with a different upstream:

```json
{
  "DevTunnel": { "AnonymousAccess": true },
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
          "primary": { "Address": "https://httpbin.org/" }
        }
      }
    }
  }
}
```

Run the AppHost again. Expected: dashboard shows both `proxy-example`/`tunnel-example` and `proxy-echo`/`tunnel-echo`. Each tunnel URL forwards to its own upstream. Stop, then delete `echo.json` before continuing.

- [ ] **Step 3: Toggle private access on one pair**

Edit `src/AppHost/proxies/example.json` and set `"DevTunnel": { "AnonymousAccess": false }`. Restart the AppHost. Expected: hitting the `tunnel-example` URL anonymously returns a sign-in page or 401 (per devtunnel's private mode). Revert the file before continuing.

- [ ] **Step 4: YARP hot-reload still works inside one pair**

With the AppHost running, edit `src/AppHost/proxies/example.json` and change the cluster destination to `https://duckduckgo.com/`. Save. Expected: within ~1s, requests to the tunnel URL forward to duckduckgo.com instead of example.com — no AppHost restart. Revert the file before continuing.

- [ ] **Step 5: Bad filename rejected at startup**

Create `src/AppHost/proxies/Bad_Slug.json` (uppercase + underscore). Run the AppHost. Expected: startup fails with `Proxy config filename 'Bad_Slug.json' is not a valid devtunnel slug ...`. Delete the file before continuing.

- [ ] **Step 6: Empty proxies folder rejected at startup**

Move `example.json` aside, leaving the folder empty. Run the AppHost. Expected: startup fails with `No proxy configurations found in '<absolute path>'. Add at least one <slug>.json file.`. Restore `example.json` before continuing.

- [ ] **Step 7: No commit needed**

This task is verification only. No code changes.

---

## Task 9: Update README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the Quickstart section**

Replace the `## Quickstart` section (lines 15-30 in current README) with:

```markdown
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
```

- [ ] **Step 2: Add an "Add another proxy" subsection**

Insert this immediately after the Quickstart section:

```markdown
### Add another proxy

Drop a second JSON file in `src/AppHost/proxies/`:

```bash
cp src/AppHost/proxies/example.json src/AppHost/proxies/api.json
# edit api.json — point Address at a different upstream
dotnet run --project src/AppHost
```

The dashboard now shows two pairs: `proxy-example` + `tunnel-example` and `proxy-api` + `tunnel-api`, each with its own public URL. Pairs are static — Ctrl+C and re-run to add or remove one.

Filename rules: lowercase letters, digits, and hyphens only; 1–32 characters; must start and end alphanumeric. Bad filenames fail AppHost startup with a clear message.
```

- [ ] **Step 3: Update the Configuration reference table**

Replace the table at `## Configuration reference` with:

```markdown
| File | Purpose |
|---|---|
| `src/AppHost/proxies/<slug>.json` | One file per proxy: tunnel access mode, YARP routes/clusters, CORS policies |
| `src/AppHost/appsettings.json` | Logging only. Optional `Proxies:Directory` to relocate the proxies folder |
| `src/Proxy/appsettings.json` | Logging only — runtime YARP/CORS config comes from the per-pair file via `Proxy__ConfigFile` |
```

- [ ] **Step 4: Update the use-case JSON examples**

Each `## Use cases` subsection currently shows a snippet labelled `// src/Proxy/appsettings.json` and edits relative to that file. Update each one to reference `// src/AppHost/proxies/<slug>.json` and show the snippet nested inside the per-pair shape (i.e. inside the `Cors` / `ReverseProxy` blocks, alongside `DevTunnel`). The snippets themselves stay identical — only the path comment and the surrounding context change.

For example, the "Add CORS to an upstream" section snippet:

```jsonc
// src/AppHost/proxies/api.json
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

Apply the same pattern (wrap snippets in the full per-pair shape, change the path comment) to: "Receive webhooks from third-party services", "Inject auth headers before forwarding", and "Inject auth fields into JSON request bodies".

- [ ] **Step 5: Update the Security section reference**

Find the line `Set DevTunnel:AnonymousAccess: false in src/AppHost/appsettings.json` and replace `src/AppHost/appsettings.json` with `the per-pair file in src/AppHost/proxies/`. The rest of that section is unchanged.

- [ ] **Step 6: Update the Project layout block**

Replace the existing tree with:

```markdown
src/
├── AppHost/
│   ├── proxies/   one .json file per public URL — drop in to add a pair
│   └── ...        .NET Aspire app host wiring proxies to dev tunnels
└── Proxy/         ASP.NET Core + YARP — reads its slice via Proxy:ConfigFile
tests/
└── Proxy.Tests/   xUnit v3 integration + unit tests
```

- [ ] **Step 7: Verify markdown renders correctly**

```bash
timeout 30 grep -n "src/Proxy/appsettings.json" README.md || echo "no remaining references"
```

Expected: `no remaining references` (every old path has been updated).

- [ ] **Step 8: Commit**

```bash
git add README.md
git commit -m "docs: update README for config-driven multi-proxy layout"
```

---

## Done

After Task 9 the branch is ready for PR. The full test suite covers helper logic and the integration boundary inside Proxy. The Aspire orchestration is verified by the manual checklist in Task 8.

Suggested PR title (Conventional Commits): `feat: support multiple proxies and devtunnels via config files`.
