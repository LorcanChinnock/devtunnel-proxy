using Proxy.Cors;
using Proxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

var slug = builder.Configuration["Proxy:Slug"]
    ?? throw new InvalidOperationException("Proxy:Slug must be set.");

builder.Services.AddReverseProxy()
       .LoadFromConfig(builder.Configuration.GetSection($"Proxies:{slug}:ReverseProxy"))
       .AddTransforms<InjectJsonFieldsTransformProvider>();

builder.Services.AddCors(options =>
    CorsPolicyConfigurator.Configure(options,
        builder.Configuration.GetSection($"Proxies:{slug}:Cors:Policies")));

var app = builder.Build();

app.UseRouting();
app.UseCors();
app.MapReverseProxy();
app.Run();

public partial class Program;
