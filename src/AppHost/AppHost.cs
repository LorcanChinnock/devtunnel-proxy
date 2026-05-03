var builder = DistributedApplication.CreateBuilder(args);

var tunnelId = builder.Configuration["DevTunnel:Id"]
    ?? throw new InvalidOperationException("DevTunnel:Id is required in appsettings.json.");

var proxy = builder.AddProject<Projects.Proxy>("proxy");

builder.AddDevTunnel("tunnel", tunnelId: tunnelId)
       .WithReference(proxy)
       .WithAnonymousAccess();

builder.Build().Run();
