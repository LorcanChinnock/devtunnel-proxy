using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proxy.Tests.Integration;

internal sealed class EchoUpstreamServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public string BaseUrl { get; }

    private EchoUpstreamServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public static async Task<EchoUpstreamServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.Run(async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            var headers = ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

            var payload = new EchoPayload
            {
                Method = ctx.Request.Method,
                Path = ctx.Request.Path.ToString(),
                ContentType = ctx.Request.ContentType,
                Headers = headers,
                Body = body,
            };

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(payload));
        });

        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new EchoUpstreamServer(app, address);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

internal sealed class EchoPayload
{
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string? ContentType { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string Body { get; set; } = "";
}
