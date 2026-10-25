using Microsoft.AspNetCore.Http;

namespace AegisWAF.Gateway.Sites;

public sealed record SiteContext(GatewaySite Site, string Hostname, string PathPrefix)
{
    public static readonly object ItemKey = new();

    public static SiteContext? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as SiteContext : null;
}
