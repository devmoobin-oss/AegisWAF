# Multi-site Summary Refresh Efficiency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Remove the Desktop Multi-site trend-refresh N+1 request pattern.

**Architecture:** Use the existing bounded summary endpoint as the single trend projection source and map its rows to the existing SiteRow view model. If the summary call fails, keep Site rows visible with the existing no-data state.

**Tech Stack:** .NET 10, WPF, typed ApiClient, xUnit in-process runner.

**Spec:** `docs/superpowers/specs/2026-10-26-multisite-summary-refresh-efficiency-design.md`

### Task 1: Contract and refactor

- [ ] Add a failing Desktop contract that requires `summary.Sites` and rejects per-Site trend calls.
- [ ] Refactor `SitesView` to map summary rows in one refresh path.
- [ ] Run Desktop contract and build.

### Task 2: Regression and handoff

- [ ] Run focused and full regression.
- [ ] Record Module 18 before/after and evidence in `handoff.md`.
