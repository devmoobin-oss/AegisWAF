# Release Manifest Verification Design

## Goal

Prevent the hosted Desktop release job from uploading a bundle that no longer matches its generated provenance manifest.

## Scope

- Keep the existing SHA-256 manifest writer unchanged.
- Add a fail-closed verification step after manifest generation and before `actions/upload-artifact`.
- Require the source-clean flag in hosted verification.
- Reuse `tools/release/Verify-ReleaseManifest.ps1`; do not add dependencies or change runtime behavior.

## Approaches

1. Verify after upload: rejected because an invalid artifact would already be available to consumers.
2. Verify before upload: selected because it blocks missing, extra, tampered, or mismatched files at the release boundary.
3. Add Authenticode signing: deferred because it requires an approved certificate/signing service and is a separate trust decision.

## Acceptance

The workflow contract must call the verifier with `-RequireCleanSource` before upload. The verifier must pass for the current published bundle, and the full test suite must remain green.

