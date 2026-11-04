# Documentation Contract Alignment Design

## Goal

Ensure the security/readiness documentation describes the current Multi-Site runtime and audit-integrity behavior instead of preserving superseded restart/open-finding claims.

## Scope

- Keep live Site activation semantics unchanged: a committed Site mutation is validated, persisted, and activated by the runtime listener without Gateway restart.
- Update `docs/PRODUCTION_READINESS.md` to describe live activation and the remaining external multi-instance boundary.
- Update `docs/SECURITY.md` to distinguish the implemented local audit chain/integrity alert from still-pending SIEM operations.
- Add contract assertions so the stale claims cannot silently return.

## Approaches

1. Change runtime back to restart-required: rejected because it conflicts with the requested Multi-Site behavior and existing live-activation tests.
2. Update documentation and enforce it with contract tests: selected because it aligns operator guidance with verified behavior without changing runtime risk boundaries.
3. Update prose without tests: rejected because stale claims could recur unnoticed.

## Acceptance

The documentation contract test must fail against the current stale text, pass after correction, and the full regression suite must remain green. The docs must continue to state that external multi-instance, SIEM, penetration, and CodeQL evidence remain pending where applicable.

