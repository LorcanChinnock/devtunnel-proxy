using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Proxy.Cors;

internal static class CorsPolicyConfigurator
{
    public static void Configure(CorsOptions options, IConfiguration policiesSection)
    {
        var policies = policiesSection.GetChildren();
        foreach (var policySection in policies)
        {
            var name = policySection.Key;
            var origins = policySection.GetSection("AllowedOrigins").Get<string[]>() ?? [];
            var methods = policySection.GetSection("AllowedMethods").Get<string[]>() ?? [];
            var headers = policySection.GetSection("AllowedHeaders").Get<string[]>() ?? [];
            var exposed = policySection.GetSection("ExposedHeaders").Get<string[]>() ?? [];
            var allowCredentials = policySection.GetValue<bool>("AllowCredentials");
            var preflightMaxAgeSeconds = policySection.GetValue<int?>("PreflightMaxAgeSeconds");

            if (allowCredentials && origins.Contains("*"))
            {
                throw new InvalidOperationException(
                    $"CORS policy '{name}' has AllowCredentials=true with AllowedOrigins '*'. " +
                    "Specify explicit origins when credentials are allowed.");
            }

            options.AddPolicy(name, policy =>
            {
                if (origins is ["*"]) policy.AllowAnyOrigin(); else if (origins.Length > 0) policy.WithOrigins(origins);
                if (methods is ["*"]) policy.AllowAnyMethod(); else if (methods.Length > 0) policy.WithMethods(methods);
                if (headers is ["*"]) policy.AllowAnyHeader(); else if (headers.Length > 0) policy.WithHeaders(headers);
                if (exposed.Length > 0) policy.WithExposedHeaders(exposed);
                if (allowCredentials) policy.AllowCredentials();
                if (preflightMaxAgeSeconds is int seconds) policy.SetPreflightMaxAge(TimeSpan.FromSeconds(seconds));
            });
        }
    }
}
