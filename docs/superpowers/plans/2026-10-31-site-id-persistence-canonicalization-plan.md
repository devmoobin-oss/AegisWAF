# Module 208 implementation plan

- [x] Trace the Site ID write path from `SiteDatabaseStore` through
      `SitePersistenceCodec.ToRow`.
- [x] Add a RED test proving a whitespace/case-variant Site ID was written
      unchanged.
- [x] Canonicalize the Site ID at the persistence row boundary.
- [x] Run focused persistence tests and the Multi-Site source regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate the behavior through a live PostgreSQL/Browser/EXE flow.
