# Multi-site Summary Failure Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add bounded summary failure telemetry and an explicit Desktop retry/recovery affordance.

**Architecture:** A process-local telemetry object holds numeric counters only. The summary endpoint reports counters on success and a safe failure header on error; Desktop shows one manual retry button and keeps all Site rows visible.

**Tech Stack:** .NET 10, ASP.NET Minimal APIs, WPF, xUnit in-process runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-summary-recovery-design.md`

### Task 1: Telemetry and contracts

- [ ] Add failing telemetry tests for failure increments, success reset, and bounded saturation.
- [ ] Add API/UI contract assertions for numeric counters and retry controls.
- [ ] Run focused tests and observe expected missing behavior.

### Task 2: Implementation

- [ ] Implement process-local numeric telemetry and register it in the gateway.
- [ ] Return safe counters on summary success and a safe failure header on error.
- [ ] Add one manual Desktop retry button with disabled/loading states.

### Task 3: Verification and handoff

- [ ] Run focused tests, build, and full regression.
- [ ] Record Module 20 before/after and evidence in `handoff.md`.
