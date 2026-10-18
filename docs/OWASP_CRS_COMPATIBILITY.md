# OWASP CRS Compatibility Study (v0.9)

Status label: **study / decision record** — no CRS code is bundled. This documents whether and how AegisWAF could adopt the [OWASP Core Rule Set](https://coreruleset.org/), so the roadmap item "OWASP CRS compatibility study" is closed with a decision rather than a half-built import.

## 1. What CRS is, and what AegisWAF is today
| | OWASP CRS | AegisWAF v0.9 engine |
|---|---|---|
| Form | ~900 generic rules written in **ModSecurity SecLang** (`SecRule …`), also runnable on Coraza (Go) and other engines that implement SecLang | ~16 built-in signature groups + anomaly scorer, written as **.NET `Regex` (NonBacktracking)** in `Program.cs` |
| Detection model | **Anomaly scoring** by default: each rule adds a score; the request is blocked when the total crosses an inbound/outbound threshold. **Paranoia levels (PL1–PL4)** trade false positives for coverage | Mostly **binary** (any signature match blocks); an **optional** additive anomaly scorer (`anomaly-01`, off by default) is a small analogue of CRS scoring |
| Transforms | Rich transformation pipeline (`t:urlDecodeUni`, `t:htmlEntityDecode`, `t:cmdLine`, `t:normalizePath`, `t:compressWhitespace`, `t:lowercase`, base64, …) applied per rule | `Normalizer`: multi-pass percent-decode, `+`→space, HTML-entity decode, SQL `/**/` strip, whitespace collapse, lowercase, **NFKC Unicode fold**, zero-width stripping — applied once, globally, producing a few canonical variants |
| Coverage | SQLi, XSS, RCE, LFI/RFI, PHP/Java/Node injection, SSRF, protocol enforcement, scanner detection, **outbound data-leakage rules (RESPONSE-95x)**, multipart/libinjection | SQLi, XSS, Path, LFI/RFI, cmd, SSRF, NoSQL, XXE, SSTI, protocol anomaly, scanner UA, **outbound leak suppression (`ResponseInspector`)** |
| Maturity | Years of tuning, a test corpus (`ftw`/`go-ftw`), CVEs tracked, active maintainers | Prototype; signatures hand-written; `CatalogVerificationTests`/`EngineUpgradeTests` as the regression corpus |

The two overlap in intent. CRS is far larger, more thoroughly tuned, and community-maintained; AegisWAF covers the common classes with much less breadth but with linear-time regex and an all-parts-normalised inspection path.

## 2. Can CRS run inside AegisWAF directly?
**No, not as-is.** CRS rules are SecLang, and there is no SecLang interpreter in .NET today:
- **libModSecurity (v3)** is a C library; a managed WAF would need P/Invoke and native build artefacts per platform — heavy for a single-file Windows app.
- **Coraza** is a mature Go SecLang engine, but it is Go. It cannot be loaded into this .NET process; it would run as a **separate process/sidecar**.
- Porting SecLang semantics (operators like `@detectSQLi`/libinjection, `@rx`, `@pmFromFile`, the transformation cache, phase model, chained rules, `ctl:` actions) into .NET is effectively writing a second engine.

So "load the CRS `.conf` files" is not reachable without either a native dependency or a sidecar.

## 3. Options evaluated
**A. Coraza sidecar (reverse-proxy in front of, or beside, the gateway).** Highest real coverage: full CRS, maintained upstream, proven. Cost: a second runtime (Go), a second process to supervise, loses the single-exe deployment story, and duplicates the proxy hop. Fits a future containerised deployment, not the current desktop app.

**B. Port a curated CRS subset to native regex.** Keep the single-process model; translate the highest-value CRS rules (e.g. the SQLi/XSS PL1 set and the RESPONSE-95x leakage rules) into NonBacktracking `Regex`. Cost: manual, must track CRS releases, and `@detectSQLi` (libinjection) has no regex equivalent — a straight port loses its accuracy. This is essentially what v0.7.1/v0.9 already did, informed by CRS categories.

**C. Adopt CRS *concepts* without its rules (chosen for now).** Keep the native engine but align its shape with CRS so a later migration to (A) is smaller:
- anomaly **scoring with a threshold** → implemented (`anomaly-01`, `Waf:AnomalyScoring:*`);
- a **paranoia-level** idea → the per-rule toggles + the scorer threshold already approximate PL1 (signatures on, scorer off) vs higher PL (scorer on, lower threshold);
- **outbound/response rules** → implemented (`ResponseInspector`, RESPONSE-95x analogue);
- **normalisation parity** → the `Normalizer` now mirrors the CRS transforms that matter most (urlDecodeUni/htmlEntityDecode/compressWhitespace/lowercase/NFKC).

## 4. Decision
Adopt **Option C** for the prototype and record **Option A (Coraza sidecar)** as the path to real CRS coverage once AegisWAF has a containerised, multi-process deployment (v0.11+). Do **not** attempt Option B as a broad port: without libinjection it would be lower-accuracy than CRS and higher-maintenance than the current native set.

### Concrete follow-ups (not yet done)
1. When the deployment model allows more than one process, prototype Coraza+CRS in front of the upstream and A/B it against the native engine using a shared `go-ftw`-style corpus.
2. Grow `CatalogVerificationTests`/`EngineUpgradeTests` toward a CRS-like test corpus so native-vs-CRS comparisons are apples-to-apples.
3. If the scorer proves useful in the field, add CRS-style **rule exclusions** (per-path/per-param allowlisting) before raising the default paranoia.

## 5. Honesty note
This study does **not** claim AegisWAF is equivalent to, or a drop-in for, OWASP CRS. CRS is broader and far more battle-tested. AegisWAF implements a smaller, native approximation of the same ideas and has not been benchmarked against CRS on a shared corpus — doing that benchmark is itself follow-up #1 above.
