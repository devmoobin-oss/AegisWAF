using System.Text.RegularExpressions;

namespace AegisWAF.Tests;

public sealed class HostedEvidenceConsistencyDocumentationTests
{
    [Fact]
    public void Authoritative_documents_reference_the_latest_hosted_evidence_from_handoff()
    {
        var root = FindRepoRoot();
        var handoff = File.ReadAllText(Path.Combine(root, "handoff.md"));
        var match = Regex.Match(
            handoff,
            @"Latest Hosted verification for runtime Module \d+: run `(?<run>\d+)`, full regression `(?<regression>\d+/\d+)`, `TRX_REGRESSION_PASS`",
            RegexOptions.CultureInvariant);

        Assert.True(match.Success, "handoff.md must expose the latest Hosted runtime verification in the authoritative continuation state.");

        var run = match.Groups["run"].Value;
        var regression = match.Groups["regression"].Value;
        var documents = new[]
        {
            "docs/PROGRESS.md",
            "docs/SECURITY.md",
            "docs/PRODUCTION_READINESS.md",
            "docs/SECURITY_ASSESSMENT.md"
        };

        foreach (var relativePath in documents)
        {
            var document = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.Contains(run, document, StringComparison.Ordinal);
            Assert.Contains(regression, document, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AegisWAF.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
