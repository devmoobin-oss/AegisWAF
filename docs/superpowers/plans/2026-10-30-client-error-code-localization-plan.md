# Module 193 implementation plan

- [x] Audit the RFC7807 response path and Desktop Login error mapping.
- [x] Add RED tests for error-code preservation and localized Desktop recovery copy.
- [x] Add optional `ApiException.Code` and parse the safe response code.
- [x] Map `auth_database_unavailable` to actionable Thai Login guidance.
- [x] Run focused tests GREEN and the broader Multi-Site/auth regression.
- [x] Build Release and publish the self-contained single-file EXE.
- [ ] Validate the localized message through a real 503 response on the target machine.
