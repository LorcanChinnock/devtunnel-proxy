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
