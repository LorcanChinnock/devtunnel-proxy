using AspireTunnelProxy.Transforms;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace Proxy.Tests.Transforms;

public class InjectJsonFieldsTransformProviderTests
{
    [Fact]
    public void Apply_does_nothing_when_metadata_is_null()
    {
        var ctx = BuildBuilderContext(metadata: null);
        new InjectJsonFieldsTransformProvider().Apply(ctx);

        Assert.Empty(ctx.RequestTransforms);
    }

    [Fact]
    public void Apply_does_nothing_when_no_metadata_keys_have_prefix()
    {
        var ctx = BuildBuilderContext(metadata: new Dictionary<string, string>
        {
            ["other"] = "value",
            ["another:Key"] = "value",
        });
        new InjectJsonFieldsTransformProvider().Apply(ctx);

        Assert.Empty(ctx.RequestTransforms);
    }

    [Fact]
    public void Apply_adds_single_transform_with_extracted_keys_when_prefixed_metadata_present()
    {
        var ctx = BuildBuilderContext(metadata: new Dictionary<string, string>
        {
            ["InjectJsonField.tenant"] = "\"acme\"",
            ["InjectJsonField.count"] = "3",
        });
        new InjectJsonFieldsTransformProvider().Apply(ctx);

        var transform = Assert.Single(ctx.RequestTransforms);
        Assert.IsType<InjectJsonFieldsTransform>(transform);
    }

    [Fact]
    public void Apply_only_propagates_prefixed_metadata_entries()
    {
        var ctx = BuildBuilderContext(metadata: new Dictionary<string, string>
        {
            ["InjectJsonField.tenant"] = "\"acme\"",
            ["UnrelatedMeta"] = "ignored",
        });
        new InjectJsonFieldsTransformProvider().Apply(ctx);

        Assert.Single(ctx.RequestTransforms);
    }

    [Fact]
    public void ValidateRoute_does_not_throw_for_empty_context()
    {
        var ctx = new TransformRouteValidationContext
        {
            Route = new RouteConfig(),
            Services = null!,
        };

        new InjectJsonFieldsTransformProvider().ValidateRoute(ctx);

        Assert.Empty(ctx.Errors);
    }

    [Fact]
    public void ValidateCluster_does_not_throw_for_empty_context()
    {
        var ctx = new TransformClusterValidationContext
        {
            Cluster = new ClusterConfig(),
            Services = null!,
        };

        new InjectJsonFieldsTransformProvider().ValidateCluster(ctx);

        Assert.Empty(ctx.Errors);
    }

    private static TransformBuilderContext BuildBuilderContext(IReadOnlyDictionary<string, string>? metadata)
    {
        var route = new RouteConfig
        {
            RouteId = "test",
            ClusterId = "test",
            Match = new RouteMatch(),
            Metadata = metadata,
        };

        return new TransformBuilderContext
        {
            Route = route,
            Services = null!,
        };
    }
}
