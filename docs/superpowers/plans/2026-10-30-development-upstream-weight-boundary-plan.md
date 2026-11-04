# Module 197 implementation plan

- [x] Audit the Development configuration path for silent upstream-weight clamping.
- [x] Add a RED test proving weight `0` was accepted in Development mode.
- [x] Apply the same 1..10,000 validation boundary in all modes.
- [x] Remove the loader's silent clamp so invalid configuration cannot be rebound.
- [x] Run focused tests and the Multi-Site source regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate invalid-weight error presentation through a live authenticated Browser/EXE flow.
