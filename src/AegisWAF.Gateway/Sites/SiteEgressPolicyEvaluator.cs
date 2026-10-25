namespace AegisWAF.Gateway.Sites;

public static class SiteEgressPolicyEvaluator
{
    public static bool IsExplicitlyAllowed(GatewaySite site, Uri destination) =>
        destination.UserInfo.Length == 0 &&
        site.Egress.AllowedSchemes.Contains(destination.Scheme, StringComparer.OrdinalIgnoreCase) &&
        site.Egress.AllowedPorts.Contains(destination.Port) &&
        site.Egress.AllowedHosts.Contains(destination.Host, StringComparer.OrdinalIgnoreCase);
}
