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
