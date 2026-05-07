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
