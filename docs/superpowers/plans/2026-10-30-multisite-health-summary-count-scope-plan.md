# Module 175 — Multi-Site Health Summary count scope

## Objective

Make the Health Summary explicit about whether alert counts describe every enabled Site or only the returned page when the bounded summary is truncated.

## Scope

- Add a small server-side scope classifier with stable values: `all-enabled-sites` and `returned-sites`.
- Include `countsScope` in `/api/sites/health/summary`.
- Preserve the client model default for older responses.
- Explain the bounded-count boundary in Browser and Desktop operator surfaces.
- Keep the current bounded query; do not silently turn a 100-Site page into an unbounded health query.

## Verification

- TDD red: the contract test must fail before the classifier, API field, model field, and UI copy exist.
- TDD green: focused contract/unit tests, JavaScript syntax check, Release build, and diff whitespace check.
- Publish the completed-modules bundle and verify its release manifest and SHA-256.
- Record before/after progress and evidence in `handoff.md`.

## Boundary

This module improves accuracy of operator interpretation. It does not implement global alert aggregation beyond the bounded page; when truncated, the UI explicitly labels the displayed-page scope.
