using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace AspireTunnelProxy.Transforms;

internal sealed class InjectJsonFieldsTransform(IReadOnlyDictionary<string, string> fields) : RequestTransform
{
    public override async ValueTask ApplyAsync(RequestTransformContext context)
    {
        var request = context.HttpContext.Request;
        if (request.ContentType is null
            || !request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
            || request.ContentLength is 0)
        {
            return;
        }

        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var original = await reader.ReadToEndAsync(context.HttpContext.RequestAborted);
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(original)) return;

        JsonNode? root;
        try { root = JsonNode.Parse(original); }
        catch (JsonException) { return; }
        if (root is not JsonObject obj) return;

        foreach (var (key, value) in fields)
        {
            obj[key] = TryParseJson(value);
        }

        var merged = obj.ToJsonString();
        context.ProxyRequest.Content = new StringContent(merged, Encoding.UTF8, "application/json");
    }

    private static JsonNode? TryParseJson(string value)
    {
        try { return JsonNode.Parse(value); }
        catch (JsonException) { return JsonValue.Create(value); }
    }
}

internal sealed class InjectJsonFieldsTransformProvider : ITransformProvider
{
    private const string Prefix = "InjectJsonField:";

    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        var metadata = context.Route.Metadata;
        if (metadata is null) return;

        var fields = metadata
            .Where(kvp => kvp.Key.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key[Prefix.Length..], kvp => kvp.Value);

        if (fields.Count == 0) return;

        context.RequestTransforms.Add(new InjectJsonFieldsTransform(fields));
    }
}
