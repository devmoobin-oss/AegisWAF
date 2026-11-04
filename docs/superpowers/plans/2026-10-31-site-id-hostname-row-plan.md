# Module 209 implementation plan

- [x] Trace Site ID usage from the primary persistence row into hostname-row
      replacement.
- [x] Add a RED source contract proving hostname rows used the raw Site ID.
- [x] Normalize the Site ID once in `ReplaceHostnamesAsync` for delete/insert.
- [x] Run the cardinality contract and the Multi-Site source regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate the behavior through a live PostgreSQL/Browser/EXE flow.
