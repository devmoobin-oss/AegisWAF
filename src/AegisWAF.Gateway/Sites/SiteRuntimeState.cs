namespace AegisWAF.Gateway.Sites;

public static class SiteRuntimeState
{
    public static bool IsPendingRestart(GatewaySite persisted, GatewaySite? active)
    {
        ArgumentNullException.ThrowIfNull(persisted);
        return active is null || active.Version != persisted.Version || !SiteConfigurationEditor.AreEquivalent(active, persisted);
    }
}
