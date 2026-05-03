using DevTunnelProxy.Cors;
using DevTunnelProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

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
