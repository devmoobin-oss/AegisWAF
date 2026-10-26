# Multi-site Summary Observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add safe timing/status observability to the bounded Multi-site summary and stable Desktop loading/error states.

**Architecture:** Measure only the summary store path with a fixed 2000 ms operator budget. Serialize a small status projection and render it in the existing `SummaryText` slot; failures remain explicit and non-sensitive.

**Tech Stack:** .NET 10, ASP.NET Minimal APIs, PostgreSQL/Npgsql, WPF, xUnit in-process runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-summary-observability-design.md`

### Task 1: Red contracts

- [ ] Add API assertions for generatedAt, queryDurationMs, queryBudgetMs, withinBudget, and status.
- [ ] Add Desktop assertions for loading and timing/status copy.
- [ ] Run focused tests and observe missing contract fields.

### Task 2: Implementation

- [ ] Add optional typed summary fields and measure the bounded summary path with a fixed budget.
- [ ] Render loading, unavailable, and ready/slow states without moving the existing layout.
- [ ] Run focused tests and build green.

### Task 3: Verification and handoff

- [ ] Run PostgreSQL-focused tests and full regression.
- [ ] Record Module 19 before/after and evidence in `handoff.md`.
