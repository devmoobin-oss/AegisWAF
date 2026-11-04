# Multi-Site UI multi-upstream management

## Problem

The Site API and persistence model already support multiple upstreams with bounded weights, but the browser Multi-Site form exposes only one upstream field. Operators cannot configure failover or weighted origins from the UI and may incorrectly assume that the first upstream is the complete Site configuration.

## Goal

Expose the existing multi-upstream contract in the browser UI for create and edit flows with clear Thai instructions and deterministic client-side validation.

## Contract

- One upstream per line using `id=url@weight`; weight defaults to 100 when omitted.
- Upstream IDs must be non-empty, unique, and bounded; URLs must be absolute HTTP(S) without credentials.
- The request sends `Upstreams` for all entries and derives the egress host/port/scheme allowlists from all entries unless an explicit egress host list is provided.
- Existing single-upstream API compatibility remains: `upstreamAddress` is populated with the first upstream.
- The UI never displays secrets or raw exception details.

## Non-goals

- Do not change gateway selection, health, egress, WAF, or production validation policy.
- Do not bypass the server-side 32-upstream and weight bounds.

## Verification

- Static UI contract verifier checks the multi-upstream field, parser, request payload, edit round-trip, and Thai guidance.
- Gateway build and `git diff --check` must pass.
