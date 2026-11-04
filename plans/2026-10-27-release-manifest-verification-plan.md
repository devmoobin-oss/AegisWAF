# Release Manifest Verification Plan

1. Extend `ReleaseProvenanceContractTests` with the required pre-upload verifier contract and run the targeted test to capture the red state.
2. Add a PowerShell verifier step after `Write-ReleaseManifest.ps1` and before artifact upload.
3. Run the contract test, the existing manifest writer/verifier tests, and verify the current published bundle with `-RequireCleanSource`.
4. Run the complete .NET regression suite and TRX counter verifier.
5. Record Module 62 before/after progress and the evidence boundary in `handoff.md`.

