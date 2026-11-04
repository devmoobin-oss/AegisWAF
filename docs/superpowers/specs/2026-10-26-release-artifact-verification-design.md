# Release Artifact Verification — Design

## Goal

Verify a published Desktop bundle against its release-provenance manifest before distribution. The verifier must detect changed, missing, duplicate, unsafe, or unmanifested files and must not confuse a hash manifest with a digital signature.

## Contract

- Validate manifest version, file path safety, file length, and SHA-256 for every entry.
- Reject files present in the bundle but absent from the manifest.
- Support explicit `-RequireSigned` and `-RequireCleanSource` gates.
- Emit a machine-readable JSON result or a concise pass line.
- Keep `signed=false` honest until an approved signing certificate/service is configured.
