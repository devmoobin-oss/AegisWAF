using System.Collections.Frozen;
using Microsoft.Extensions.Configuration;

namespace AegisWAF.Gateway.Sites;

/// <summary>
/// Immutable per-request policy snapshot. Site policies can only disable rules from the global catalog;
/// they cannot silently enable a rule that an operator disabled globally.
/// </summary>
public sealed record SiteWafPolicy(string Name, IReadOnlySet<string> DisabledRuleIds)
{
    public static SiteWafPolicy Global { get; } = new("global", FrozenSet<string>.Empty);
}

public sealed class SitePolicyResolver
{
    private readonly IReadOnlyDictionary<string, SiteWafPolicy> _policies;

    public SitePolicyResolver(IConfiguration configuration)
    {
        _policies = configuration.GetSection("Waf:Policies").GetChildren()
            .Select(section => new SiteWafPolicy(
                section.Key,
                (section.GetSection("DisabledRules").Get<string[]>() ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToLowerInvariant())
                    .ToFrozenSet(StringComparer.OrdinalIgnoreCase)))
            .ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    public SiteWafPolicy Resolve(GatewaySite site) =>
        _policies.TryGetValue(site.WafPolicy, out var policy) ? policy : SiteWafPolicy.Global;
}
