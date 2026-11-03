# Module 214 — Shared Site ID Validation Rule

## Opportunity

`SiteConfigurationEditor` carried a private copy of the Site ID regular expression while `SiteConfigurationValidator` already owned the canonical Site ID rule. Separate rules could drift and allow the API editor and runtime validator to disagree.

## Contract

- The management editor delegates Site ID validity to `SiteConfigurationValidator.IsValidSiteId`.
- There is no second Site ID regex in the editor.
- Existing trim/lowercase normalization and user-facing validation text remain unchanged.

## Verification

- RED: the source contract failed because the editor did not call the shared validator.
- GREEN: the full `SiteConfigurationEditorTests` suite passed after the refactor.
