using AppHost;

namespace Proxy.Tests.Hosting;

public class ProxySlugTests
{
    [Theory]
    [InlineData("api")]
    [InlineData("a")]
    [InlineData("a1")]
    [InlineData("api-v2")]
    [InlineData("1abc")]
    [InlineData("ab")]
    public void IsValid_returns_true_for_valid_slug(string name) =>
        Assert.True(ProxySlug.IsValid(name));

    [Fact]
    public void IsValid_returns_true_for_max_length_slug() =>
        Assert.True(ProxySlug.IsValid("a" + new string('b', 30) + "c"));

    [Theory]
    [InlineData("")]
    [InlineData("Api")]
    [InlineData("-api")]
    [InlineData("api-")]
    [InlineData("api_v2")]
    [InlineData(" api ")]
    public void IsValid_returns_false_for_invalid_slug(string name) =>
        Assert.False(ProxySlug.IsValid(name));

    [Fact]
    public void IsValid_returns_false_for_too_long_slug() =>
        Assert.False(ProxySlug.IsValid(new string('a', 33)));

    [Fact]
    public void IsValid_returns_false_for_null() =>
        Assert.False(ProxySlug.IsValid(null));
}
