# Multi-site Alert Expiry and Summary Performance Design

## Intent

Close the lifecycle gap left after Module 16: expired acknowledgement rows must not accumulate indefinitely, and the cross-site summary must prove its query bounds under hostile input values.

## Goals

- Remove expired acknowledgement rows with an indexed, bounded cleanup operation.
- Return the number of expired rows removed so operators can observe cleanup without inspecting the database.
- Clamp summary windows to 64 observations/site and site results to 100.
- Keep the summary projection and raw-history retention unchanged.
- Preserve the audit chain for operator acknowledgement actions; cleanup is maintenance, not an operator action.

## Non-goals

- Background scheduling or external notification delivery.
- Deleting raw health observations.
- Increasing the existing 64-observation retention.

## Approach

Add a store cleanup method using the existing `acknowledged_until` index. The summary endpoint invokes cleanup once before reading acknowledgements and exposes `expiredAcknowledgementsRemoved`. The endpoint and store both clamp bounds, and the integration test sends oversized values to prove the returned window and site count remain safe.

## Acceptance gates

- PostgreSQL test inserts an expired acknowledgement and observes it removed through the summary contract.
- Oversized `window` and `limit` values remain capped.
- Solution/runner build, focused tests, and full regression pass.
