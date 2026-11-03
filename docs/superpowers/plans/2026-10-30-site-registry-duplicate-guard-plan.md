# Module 199 implementation plan

- [x] Audit `SiteRegistry.BuildIndex` for silent duplicate handling.
- [x] Add a RED test for IDs that collide after trim/case normalization.
- [x] Normalize registry keys consistently and throw on duplicate keys.
- [x] Run focused registry tests and the Multi-Site source regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate malformed-snapshot error presentation through a live authenticated Browser/EXE flow.
