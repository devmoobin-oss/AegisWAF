# Module 187: Multi-Site persistence upstream-value write boundary

## Goal

Make `SitePersistenceCodec.ToRow` enforce the same upstream invariants already enforced by `FromRow`, so invalid in-memory configuration cannot be serialized into persistence rows.

## Contract

Each upstream written to a persistence row must:

- use an absolute `http` or `https` URI;
- contain no user-info credentials;
- contain no query string or fragment;
- use a weight from 1 through 10,000.

Violations must throw `InvalidDataException` before a row is constructed.

## Verification

- RED: add one focused test covering an invalid scheme, credentials, and weight; it must fail before the guard exists.
- GREEN: add the write-side checks and require the focused persistence class to pass 10/10.
- Regression: run the targeted Multi-Site test set.
- Release: build and publish the self-contained Windows artifact, validate the manifest and executable provenance.

## Boundaries

This module does not claim live PostgreSQL, browser, external deployment, or Authenticode verification. The current workspace also cannot write the Git index, so commit status remains separately reported.
