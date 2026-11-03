# Module 210 — Database Site ID Boundary Plan

1. Add a source contract covering every direct Site ID database boundary and an integration regression using padded/uppercase input.
2. Run the focused tests and record the expected RED failures.
3. Normalize Site IDs at the start of store methods; use the canonical value for SQL, audit, notification, and returned acknowledgement data.
4. Run focused GREEN tests and the Multi-Site source regression.
5. Build and publish the self-contained single-file Windows artifact, verify the manifest, PE header, JavaScript syntax, and diff whitespace.
6. Append the authoritative Module 210 handoff pointer with before/after percentages, evidence, artifact hash, and remaining boundaries.
