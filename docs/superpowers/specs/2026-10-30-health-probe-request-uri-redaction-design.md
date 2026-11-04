# Module 195: Health probe request URI redaction

## Goal

Ensure the outbound health-probe URI is built from only the upstream scheme and authority plus the configured health path, so credentials cannot be sent if a malformed or legacy in-memory URL bypasses normal validation.

## Contract

- `BuildProbeUri` never carries `Uri.UserInfo` into the request URI.
- The configured health path remains the request path.
- Existing authority, port, and scheme behavior remains intact.
- Display redaction from Module 194 and request redaction are both enforced.

## Verification boundary

This module proves the URI-construction contract and Release/publish compilation. It does not claim live upstream traffic, PostgreSQL, authenticated Browser/EXE smoke, external security scans, or Authenticode signing.
