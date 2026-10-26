# Hosted security verification and release provenance design

## Context

The repository has a security-verification workflow and a local assessment runner, but a published Desktop bundle does not yet have a repeatable manifest containing its file hashes, source revision, build inputs, and working-tree state. Hosted CodeQL, secret scan, SBOM, and dependency results also need to remain explicit evidence rather than implied by local tests.

## Goal

Create a repeatable, redacted release-provenance artifact for a published bundle and make the hosted workflow able to produce it on demand. Keep unavailable external scanners and dirty-source builds visibly non-passing.

## Design

- Add a PowerShell manifest writer that recursively records relative file path, byte length, and SHA-256 for a publish directory.
- Include manifest version, UTC generation time, repository commit, dirty/clean state, target framework/runtime, and the declared build command.
- Exclude the output manifest if it is placed inside the bundle and use normalized relative paths for portable verification.
- Add a self-test script that creates a temporary fixture, validates hashes/order/metadata, mutates a file, and confirms the hash changes.
- Add an opt-in hosted workflow job that publishes the Desktop self-contained bundle and uploads the executable plus provenance manifest. The default CI path remains unchanged.

## Security constraints

- Never include connection strings, environment variables, tokens, or file contents in the manifest.
- Do not claim the manifest is a signature; record `signed=false` until an approved signing certificate/service is configured.
- A dirty working tree is recorded and can be rejected with `-RequireCleanGit`.
- External scanner output remains the authoritative evidence for CodeQL/Gitleaks/SBOM/dependency gates.

## Acceptance gates

1. Manifest self-test passes, including tamper-detection hash change.
2. Release script works against a real published directory and emits valid JSON.
3. Workflow YAML contains an opt-in publish/provenance job with artifact upload.
4. Solution build and focused regression remain green.
5. Hosted scan status is reported as pending until a real hosted run exists.
