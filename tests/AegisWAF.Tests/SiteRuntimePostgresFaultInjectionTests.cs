namespace AegisWAF.Tests;

/// <summary>
/// The real PostgreSQL stop/start fault-injection attempt is intentionally not part of the
/// normal xUnit suite. It requires an external process supervisor and remains an explicit
/// production-environment gate until its teardown can be made deterministic.
/// </summary>
public static class SiteRuntimePostgresFaultInjectionTests
{
    public const string RequiredOptInVariable = "AEGISWAF_ENABLE_PG_FAULT_INJECTION";
}
