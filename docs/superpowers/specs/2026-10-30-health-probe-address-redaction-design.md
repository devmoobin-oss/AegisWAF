# Module 194: Health probe address redaction

## Goal

Prevent health-probe results from reflecting upstream user-info, query, or fragment data to Browser/Desktop clients when an invalid or legacy in-memory object bypasses normal configuration validation.

## Contract

- Probe result addresses contain only HTTP(S) scheme, authority, and absolute path.
- Upstream user names and passwords are never included in health results.
- Query and fragment data are omitted from displayed probe addresses.
- Health history already stores only upstream IDs and remains unchanged.

## Verification boundary

This module proves the display-address redaction contract and Release/publish compilation. It does not claim live PostgreSQL, authenticated browser/EXE smoke, external security scans, or Authenticode signing.
