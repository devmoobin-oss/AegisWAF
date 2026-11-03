# Module 193: Client error-code localization

## Goal

Preserve the server's stable RFC7807 error code through `ApiClient` so Desktop can select localized, actionable recovery guidance without parsing English text or exposing infrastructure details.

## Contract

- `ApiException.Code` is optional, trimmed, and null when absent or blank.
- JSON `code` is preserved for both legacy `error` responses and RFC7807 `title/detail` responses.
- Existing status/message behavior remains unchanged.
- Desktop Login maps `auth_database_unavailable` to Thai PostgreSQL/connection-string guidance and leaves unrelated errors unchanged.

## Verification boundary

This module proves Client/Desktop source contracts and Release/publish compilation. It does not claim live PostgreSQL login or authenticated published-EXE UI smoke.
