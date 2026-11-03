# Module 201 implementation plan

- [x] Audit the accepted weight range against `SiteHealthRegistry.Select`.
- [x] Add a RED test proving a valid weight of 10,000 was silently clamped.
- [x] Define one shared maximum and apply it to validation, persistence, editor, and health selection.
- [x] Run focused health tests and the Multi-Site source regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate weighted routing through a live authenticated Browser/EXE flow with multiple upstreams.
