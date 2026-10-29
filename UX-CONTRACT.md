# AegisWAF Web Console UX Contract

## Product context

- Audience: operators and security administrators managing the local AegisWAF Gateway.
- Primary jobs: inspect traffic, manage Multi-Site routes, review Health state, and perform authorized security administration.
- Target market(s): local/deployed Windows security operations; no Japan-specific market behavior is claimed.
- Active locales: Thai-first UI with concise English technical identifiers and protocol terms.
- Language/content register: operational, explicit, recovery-oriented; domain copy is maintained in the web assets.
- Timezone/calendar policy: server/API timestamps are authoritative; browser formatting is display-only.
- Accessibility target: WCAG 2.2 AA baseline.

## Business-context sources

| Domain / scope | Authoritative source | Source type | Reviewed date |
|---|---|---|---|
| Permission model | `docs/ARCHITECTURE.md#authorization-matrix` | Architecture / permission policy | 2026-10-30 |
| Multi-Site lifecycle and routes | `docs/superpowers/specs/2026-10-18-multi-site-gateway-design.md` | API / domain specification | 2026-10-30 |
| Health and activation semantics | `docs/PRODUCTION_READINESS.md` and `docs/superpowers/specs/2026-10-26-multisite-ui-truthful-activation-design.md` | Operational / UI specification | 2026-10-30 |
| Deletion and retention | `docs/SECURITY.md` and `docs/PRODUCTION_READINESS.md` | Security / retention policy | 2026-10-30 |

## Visual contract

- Project `DESIGN.md`: incumbent WPF design system; the browser console retains its established dark operational UI.
- Token ownership model: existing browser runtime CSS tokens in `src/AegisWAF.Gateway/wwwroot/assets/style.css`.
- Runtime design-system/token source: `style.css`; browser behavior and copy live in `app.js`/`index.html`.
- Mapping/export/adapters: no generated token pipeline; CSS custom properties are the browser source.
- Token drift gate: inspect changed CSS with the Impeccable detector and preserve existing palette unless a named system decision is recorded.
- Supported themes: dark operational console; system high-contrast behavior remains platform-owned.

## Canonical UI Map

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
|---|---|---|---|---|
| Table Selection | semantic HTML tables and the Site Manager table renderer | `src/AegisWAF.Gateway/wwwroot/assets/app.js` | page | focused UI contracts + browser smoke |
| Select/Listbox | native HTML `<select>` | `src/AegisWAF.Gateway/wwwroot/index.html` and `premium-ui.json` | native | keyboard + platform popup |
| Form | native HTML forms with application validation | `index.html`, `app.js`, server validation | create / edit | validation contracts + browser smoke |
| Scrollbar | global CSS scrollbar baseline with `nav` geometry exception | `src/AegisWAF.Gateway/wwwroot/assets/style.css` | geometry exceptions | standards properties + browser computed style |
| Toast | `toast()` and `#toastRegion` live region | `src/AegisWAF.Gateway/wwwroot/assets/app.js`/`index.html` | success / warning / info / error | live-region contract |
| CRUD | `loadSitesCrud` and `/api/sites` route/service behavior | `app.js`, `GatewayApp.cs`, `SiteDatabaseStore.cs` | return / stay | create/edit/delete and failure contracts |

## Component behavior

| Component | Default | Hover | Focus | Active | Disabled | Busy | Error |
|---|---|---|---|---|---|---|---|
| Button | native action semantics | incumbent hover token | visible browser/app focus | pressed | disabled without resizing | `disabled` during mutation | scoped inline status |
| Input | labeled value | incumbent border | visible focus | edited value | disabled | mutation owner controls | inline status and recovery |
| Textarea | `resize: none` | incumbent border | visible focus | edited value | disabled | mutation owner controls | inline validation |
| Table/list | authoritative API data | row hover | keyboard native controls | selected/editing row | unavailable actions disabled | progressive loading status | persistent page notice |

## Dataset navigation

- Admin tables: Multi-Site list is bounded by the API contract; Health summary declares total/returned/truncated metadata.
- URL state: transient view state remains in the current page; no sensitive values are put in URLs.
- Empty/loading/error treatment: show the Site list as soon as `/api/sites` succeeds; show summary loading/unavailable state separately; show an actionable empty state.
- Back/scroll restoration: return to the Multi-Site page without discarding an in-progress form unless the user explicitly cancels.
- Selection scope: no bulk selection; each row owns its Site actions.

## Flow ledger

| Operation | Trigger | Pending | Success destination | Success feedback | Failure recovery | Focus outcome | Source ref |
|---|---|---|---|---|---|---|---|
| Create | Site form submit | submit disabled; mutation guarded | same Multi-Site page | inline success | authoritative reload | form remains usable | `docs/superpowers/specs/2026-10-26-multisite-ui-truthful-activation-design.md` |
| Edit | row Edit | form populated | same Multi-Site page | inline success | conflict/error message and reload | Site ID remains read-only | `docs/ARCHITECTURE.md#multi-site-request-model` |
| Delete | row Delete + confirmation | confirmation pending | same Multi-Site page | inline success | retain row and explain failure | action row restored | `docs/SECURITY.md` |
| Health probe | row Health | row action disabled | same Multi-Site page | inline status with persistence boundary | explain timeout/database failure | action row restored | `docs/PRODUCTION_READINESS.md` |

## Async and resilience

- Mutation default: pessimistic; server confirmation is required before success is shown.
- Duplicate-submit policy: form mutation guard and disabled submit control.
- Offline/read-stale/write behavior: label unavailable Health summary; do not queue security mutations.
- Retry/timeout behavior: server budgets and cancellation are authoritative; user-triggered reload is the retry path.
- Version conflict: Site mutations use expected version/ETag and must not overwrite a newer row silently.
- Session expiry: 401 returns through the approved login flow; 403 remains an authorization boundary.
- Stale-request cancellation: view generation and `AbortController` prevent late responses from overwriting a newer view.

## Validation

- Schema/validation layer: client guidance plus server `SiteConfigurationEditor` validation.
- Trigger timing: submit-time for the Site form; server response remains authoritative.
- Error policy: inline Thai message with recovery guidance; raw database details are not exposed.
- `noValidate`: application-owned login, change-password, and dynamic Site forms use app validation rather than native browser bubbles.

## Verification

- Static commands: `node --check src/AegisWAF.Gateway/wwwroot/assets/app.js`; premium audit command recorded in `handoff.md`.
- Browser matrix: authenticated success, summary unavailable/slow, empty list, keyboard controls, narrow viewport, and session expiry remain required smoke cases.
- Canonical sibling flow: dashboard refresh and existing security-rule CRUD feedback patterns.
- Project audit result: strict audit is rerun after contract/config changes; remaining dynamic-button detector findings are recorded as static-analysis limitations, not masked with fake handlers.
