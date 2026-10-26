# Multi-Site UI truthful activation and health copy

## Problem

The current Multi-Site browser UI still tells operators that saving a Site requires a Gateway restart, while the API already persists and activates the route immediately. The stale copy can make a successful add/edit/delete look incomplete and makes health states such as `critical` or `degraded` difficult to interpret.

## Goal

Make the Multi-Site UI match the live API contract and expose bounded Thai-language explanations for the operational state without changing routing or WAF decisions.

## Non-goals

- Do not weaken egress, authentication, WAF, or production readiness policies.
- Do not turn a health display label into a health decision.
- Do not expose upstream credentials, raw exception text, or sensitive connection details.

## Contract

- Successful POST/PUT/DELETE responses are rendered as immediately active/deactivated; no restart instruction is shown.
- `pending-restart` remains a supported defensive state and is rendered as “รอเปิดใช้” when the API explicitly returns it.
- Health labels map known states to Thai copy: healthy = ปกติ, degraded = เฝ้าระวัง, critical = วิกฤต, unhealthy = ไม่พร้อม, unknown = ไม่ทราบ, no-data = ยังไม่มีข้อมูล, recovered = กู้คืนแล้ว.
- The UI shows a bounded reason based only on safe API fields (`alertState`, `recoveryState`, `consecutiveUnhealthy`, `healthy`, `unhealthy`, `unknown`, `isStale`); it does not render raw upstream exception messages.
- Existing API verbs and endpoints remain unchanged.

## Verification

- Contract tests assert immediate-activation copy, pending-state fallback, Thai state labels, and absence of stale restart guidance.
- Gateway build and `git diff --check` must pass.
