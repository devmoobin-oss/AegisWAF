# Invalid persisted snapshot isolation design

## Opportunity

The Multi-Site listener already validates a complete PostgreSQL snapshot before publishing it. The remaining confidence gap was an end-to-end regression proving that a malformed persisted upstream cannot replace a peer's active route, and that a later valid notification restores convergence without restarting either Gateway.

## Design

- Start two Gateway instances against the same disposable PostgreSQL database.
- Create a valid Site through the API and wait for the peer registry to converge.
- Mutate only the persisted upstream address to an invalid non-HTTP value and send the reviewed redacted change notification.
- Assert that the peer records a bounded `InvalidDataException` and retains the previous valid route.
- Repair the persisted address, send another notification, and assert that the peer reloads the valid route.
- Always delete the test Site in `finally`; the test never changes production configuration or the user's database.

## Acceptance criteria

- The invalid snapshot test passes over real PostgreSQL and two Gateway instances.
- The active route remains last-known-good during invalid reload.
- Recovery occurs after a valid committed snapshot without process restart.
- The full regression suite remains green with exact counts recorded in evidence.
