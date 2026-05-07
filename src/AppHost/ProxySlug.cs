using System.Text.RegularExpressions;

namespace AppHost;

public static partial class ProxySlug
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,30}[a-z0-9])?$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);
}
