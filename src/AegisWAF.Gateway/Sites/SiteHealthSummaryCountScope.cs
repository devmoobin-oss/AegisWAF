namespace AegisWAF.Gateway.Sites;

public static class SiteHealthSummaryCountScope
{
    public const string AllEnabledSites = "all-enabled-sites";
    public const string ReturnedSites = "returned-sites";

    public static string For(long totalSites, int returnedSites) =>
        totalSites > returnedSites ? ReturnedSites : AllEnabledSites;
}
