# Multi-Site UI per-Site policy fields

## Problem

The Gateway already persists and validates per-Site health-check and rate-limit policies, but the browser form sends neither. Every Site created or edited through the UI silently falls back to defaults, so operators cannot configure a dedicated health path, probe timing, passive reactivation, or Site-specific request limits.

## Goal

Expose the existing per-Site Health and Rate Limit contract in the Multi-Site browser form with Thai explanations and client-side checks matching server bounds.

## Bounds

- Health path: relative path, 1–128 characters, no query or fragment.
- Health interval: 5–300 seconds.
- Health timeout: 1–30 seconds and less than interval.
- Passive reactivation: 5–3600 seconds.
- Site requests: 1–1,000,000.
- Rate window: 1–3600 seconds.
- Global Site requests: 1–10,000,000.

## Safety

- Server-side validation remains authoritative.
- No private-egress exception control is exposed in this form.
- Existing API compatibility and immediate route activation remain unchanged.

## Verification

- Static UI contract checks cover fields, Thai guidance, payload names, bounds, and edit round-trip.
- JavaScript syntax, Gateway build, and `git diff --check` must pass.
