# Module 206 implementation plan

- [x] Audit expected upstream count lookup in the comparison-alert path.
- [x] Add a RED test proving a whitespace-padded Site ID loses completeness
      context.
- [x] Normalize the Site ID only for expected-count lookup while preserving
      the display value.
- [x] Run focused alert tests and the Multi-Site source regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate the behavior through a live authenticated Browser/EXE flow.
