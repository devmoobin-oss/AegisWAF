# Audit Integrity Alerting Design

## Context

The gateway already stores chained audit rows and exposes `GET /api/audit/integrity`.
When the chain is broken, the endpoint returns `valid=false`, but the result is only
visible to the caller. A monitoring system cannot reliably detect tampering unless the
check also emits the existing structured security event used by the SIEM integration.

## Goal

When an authenticated integrity check detects a broken audit chain, emit one structured
`Audit Integrity Failure` security event containing the first invalid row id and the
request id. Do not append a new audit row, because writing into a compromised chain
would make the evidence less trustworthy.

## Non-goals

- Repairing, deleting, or rewriting audit rows.
- Running a background integrity scan on a schedule.
- Treating a database outage as proof of audit tampering; database errors remain a 503.
- Changing the existing response shape or administrator roles.

## Options considered

1. Keep the current endpoint-only result. This preserves behavior but leaves SIEM blind
   unless an operator polls and interprets the response.
2. Write an audit row for the failure. This would contaminate the chain being reported
   and is rejected.
3. Emit the existing security event and deduplicate by invalid row id. This preserves
   the evidence, reaches structured logging/database event persistence, and avoids a
   polling storm generating duplicate alerts. This is the selected approach.

## Decision

Add a small in-memory `AuditIntegrityAlertState` singleton. The endpoint emits a
security event only when the result is invalid and its `firstInvalidId` has not already
been alerted among the most recent bounded set of ids. The event uses system identity,
status 500, the endpoint path, and a bounded rule string containing the invalid id. A
new invalid id alerts again, so a later corruption is not hidden by the first alert,
while the bounded set prevents an attacker from growing alert state without limit.
If the bounded security-event queue rejects the event, the claim is released so a later
integrity check can retry delivery instead of permanently suppressing the alert.

## Verification

- Unit tests cover first alert, duplicate suppression, and a new invalid id.
- An endpoint-level regression test is not required because database-backed endpoint
  tests use the same event queue and would require destructive chain mutation.
- Module verification must include gateway build, focused tests, and documentation
  contract checks.
