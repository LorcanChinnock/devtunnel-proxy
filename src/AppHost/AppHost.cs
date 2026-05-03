using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var tunnelId = builder.Configuration["DevTunnel:Id"]
    ?? throw new InvalidOperationException("DevTunnel:Id is required in appsettings.json.");

var anonymousAccess = builder.Configuration.GetValue("DevTunnel:AnonymousAccess", true);

var proxy = builder.AddProject<Projects.Proxy>("proxy");

var tunnel = builder.AddDevTunnel("tunnel", tunnelId: tunnelId)
                    .WithReference(proxy);

if (anonymousAccess)
{
    tunnel.WithAnonymousAccess();
}

builder.Build().Run();
