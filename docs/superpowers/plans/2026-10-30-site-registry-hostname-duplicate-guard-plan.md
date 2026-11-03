# Module 200 implementation plan

- [x] Audit `SiteRegistry.BuildHostnameIndex` for silent duplicate handling.
- [x] Add a RED test for hostnames that collide after normalization.
- [x] Throw an explicit error instead of selecting the first hostname owner.
- [x] Run focused registry tests and the Multi-Site source regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate malformed-snapshot error presentation through a live authenticated Browser/EXE flow.
