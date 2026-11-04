---
name: AegisWAF Desktop Console
description: Native Windows security console that draws the WAF as a transit line every request rides.
colors:
  midnight-ground: "#0B1230"
  rail-ink: "#08102A"
  enamel-panel: "#111B40"
  plate-raised: "#16224D"
  line-divider: "#26346A"
  line-strong: "#34437A"
  edge: "#4A5A92"
  porcelain: "#F4F3EE"
  muted: "#AEB6CE"
  faint: "#8790AE"
  input-well: "#0D163A"
  hover: "#1B2858"
  selected: "#1C2C66"
  cobalt: "#1E5BFF"
  cobalt-hover: "#3A70FF"
  amber: "#FFC20E"
  line-green: "#22B14C"
  violet: "#8A6BD8"
  slate: "#7D87A8"
  scarlet: "#E21D2D"
  cobalt-text: "#7EA2FF"
  alert-text: "#FF7466"
  green-text: "#3FCB6A"
typography:
  display:
    fontFamily: "Bahnschrift, Noto Sans Thai, Leelawadee UI"
    fontSize: "24px"
    fontWeight: 600
  headline:
    fontFamily: "Bahnschrift, Noto Sans Thai, Leelawadee UI"
    fontSize: "18px"
    fontWeight: 600
  title:
    fontFamily: "Bahnschrift, Noto Sans Thai, Leelawadee UI"
    fontSize: "16px"
    fontWeight: 600
  figure:
    fontFamily: "Bahnschrift, Noto Sans Thai, Leelawadee UI"
    fontSize: "22px"
    fontWeight: 600
    fontFeature: "tnum"
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, Leelawadee UI"
    fontSize: "13px"
    fontWeight: 400
  label:
    fontFamily: "Segoe UI Variable Text, Segoe UI, Leelawadee UI"
    fontSize: "12.5px"
    fontWeight: 400
  mono:
    fontFamily: "Cascadia Mono, Consolas"
    fontSize: "12.5px"
    fontWeight: 400
rounded:
  xs: "3px"
  sm: "4px"
  md: "6px"
  full: "9999px"
spacing:
  gap: "16px"
  card-x: "20px"
  card-y: "18px"
  gutter: "28px"
  rail: "256px"
  bar: "84px"
components:
  button-primary:
    backgroundColor: "{colors.cobalt}"
    textColor: "#FFFFFF"
    typography: "{typography.title}"
    rounded: "{rounded.sm}"
    padding: "8px 16px"
    height: "34px"
  button-primary-hover:
    backgroundColor: "{colors.cobalt-hover}"
  button-ghost:
    backgroundColor: "transparent"
    textColor: "{colors.porcelain}"
    rounded: "{rounded.sm}"
    padding: "8px 16px"
    height: "34px"
  button-ghost-hover:
    backgroundColor: "{colors.hover}"
  button-danger:
    backgroundColor: "transparent"
    textColor: "{colors.porcelain}"
    rounded: "{rounded.sm}"
    height: "34px"
  button-danger-hover:
    textColor: "{colors.alert-text}"
  button-small:
    padding: "3px 10px"
    height: "26px"
  input:
    backgroundColor: "{colors.input-well}"
    textColor: "{colors.porcelain}"
    typography: "{typography.body}"
    rounded: "{rounded.sm}"
    padding: "2px 10px"
    height: "34px"
  card:
    backgroundColor: "{colors.enamel-panel}"
    rounded: "{rounded.md}"
    padding: "18px 20px"
  state-pill-attack:
    backgroundColor: "{colors.scarlet}"
    textColor: "#FFFFFF"
    rounded: "{rounded.sm}"
    padding: "7px 12px"
  state-pill-watch:
    backgroundColor: "{colors.amber}"
    textColor: "{colors.midnight-ground}"
    rounded: "{rounded.sm}"
    padding: "7px 12px"
  state-pill-calm:
    backgroundColor: "transparent"
    textColor: "{colors.porcelain}"
    rounded: "{rounded.sm}"
    padding: "7px 12px"
  roundel:
    rounded: "{rounded.full}"
    size: "26px"
  tag:
    backgroundColor: "transparent"
    textColor: "{colors.muted}"
    rounded: "{rounded.xs}"
    padding: "3px 8px"
  nav-item:
    textColor: "{colors.muted}"
    height: "42px"
  nav-item-active:
    backgroundColor: "{colors.enamel-panel}"
    textColor: "{colors.porcelain}"
---

# Design System: AegisWAF Desktop Console

Scope: the native WPF app in `src/AegisWAF.Desktop` (setup wizard, login, change-password and prompt dialogs, main shell and its nine pages). Source of truth is `Theme.xaml` (brushes, fonts, control styles), `Lines.cs` (line inks, roundel codes, inspection stations) and `ThreatState.cs` (attack-state rule). The browser dashboard in `src/AegisWAF.Gateway/wwwroot` is not covered by this system and keeps its own older look.

## Overview

**Creative North Star: "The Midnight Transit Diagram"**

The WAF is a line every request rides. It enters at the gateway address, passes the inspection stations, and is either stopped at a station or reaches the real site. The console draws that line live. Everything else (navigation, panels, roundels, checkboxes) borrows from the same night-time wayfinding sheet: midnight enamel ground, porcelain ink and edges, a small set of line inks that say what kind of thing you are looking at, and the DIN signage face for every label and number.

It is an all-day operations console for SOC analysts, so density is moderate and calm is the default. The one question the screen answers at a glance is "are we under attack right now". That answer has its own colour, and nothing else may borrow it. The world rejects the neon-on-black hacker console, the generic KPI-card SaaS grid, and a console that is permanently red.

**Key Characteristics:**
- Midnight enamel ground with flat panels framed in porcelain at 30%; one elevation, no shadows.
- Four line inks plus slate carry category and series meaning; scarlet carries only the live attack state.
- Bahnschrift (Noto Sans Thai for Thai) for headings, labels and figures; Segoe UI / Leelawadee UI for body; Cascadia Mono for IPs, URLs and rule IDs.
- Station rings and line roundels are the shared vocabulary: nav, checkboxes, OWASP coverage and event rows all speak it.
- Every control that takes input or triggers an action stands 34px tall.

## Colors

A deep blue-black enamel with porcelain ink, four saturated transit inks, and one alarm colour held in reserve.

### Primary
- **Cobalt Line** (#1E5BFF): the primary action fill (default buttons, checked checkbox, active nav ring stroke), the inspection-line track, and the "requests" series in charts. Hover lifts to **Cobalt Lit** (#3A70FF).
- **Cobalt Signal Text** (#7EA2FF): the text-safe cobalt on midnight; links, expander headers, rule-ID chips, and the single focus ring colour.

### Secondary
- **Amber Line** (#FFC20E): XSS / SSTI category ink; the "blocked" series in the traffic chart; the "watch" state (pill fill with midnight text, station ring and figure when a station stopped something in the last 5 minutes but the attack threshold is not met); partial coverage.
- **Line Green** (#22B14C): path traversal / LFI / XXE category ink; full coverage. Text-safe **Signal Green** (#3FCB6A) is used for the gateway-running status and success messages.
- **Violet Line** (#8A6BD8): command injection / SSRF / protocol anomaly / response leak category ink.
- **Slate Line** (#7D87A8): scanner / size and time limits / anomaly score / IP and rate limit / custom rules; also the hollow "not covered" ring.

### Tertiary
- **Alarm Scarlet** (#E21D2D): the live "under attack" state, and nothing else. It fills the state pill (white text) and rings the station(s) that stopped requests while the attack state is live; the station figure and "N ใน 5 นาที" note switch to Alert Coral alongside it.
- **Alert Coral** (#FF7466): text-safe red for error messages, the destructive-button hover, and the outlined "Gateway offline" state pill. It is a message colour, not a fill.

### Neutral
- **Midnight Enamel** (#0B1230): window ground, title bar (painted via DWM), tooltip text.
- **Rail Ink** (#08102A): the left navigation rail.
- **Enamel Panel** (#111B40): every card/panel surface, combo dropdown, active nav row.
- **Raised Plate** (#16224D): notices and row hover.
- **Line Divider** (#26346A): internal dividers only (rail and top-bar rules, grid row rules, panel inner hairlines, bar-chart tracks).
- **Line Strong** (#34437A): input, tag and chip strokes; scrollbar thumb.
- **Edge** (#4A5A92): ghost and destructive button outlines, grid header rule, idle station ring, hover stroke on inputs.
- **Porcelain** (#F4F3EE): primary text, terminus/interchange markers, active nav station fill, tooltip background, panel edge at 30% opacity.
- **Muted** (#AEB6CE) and **Faint** (#8790AE): secondary text and captions; placeholders and zero-count figures.
- **Input Well** (#0D163A), **Hover** (#1B2858), **Selected** (#1C2C66): field fill, ghost hover, selected rows and highlighted dropdown items.

### Named Rules
**The One Job Rule.** Scarlet (#E21D2D) appears only when the live attack state is on: the state pill fill and the stopping station's ring. Primary buttons are cobalt, destructive buttons are Edge-outlined ghosts that turn Alert Coral only under the pointer, and "not covered" is a hollow slate ring. If scarlet is on screen while the state pill says ปกติ, something is wrong.

**The Line Ink Rule.** Category inks are fixed by `Lines.Of`: cobalt = SQLi/NoSQL, amber = XSS/SSTI, green = path/LFI/XXE, violet = command/SSRF/protocol/response leak, slate = scanner/limits/anomaly/IP/custom. A category always wears the same ink and two-letter roundel code on every page. Outside category views the inks also serve as series colours (requests cobalt, blocked amber; the HTTP-method bar takes cobalt, amber, green, violet, slate in order).

**The Calm Default Rule.** Calm reads as an outline (green edge, porcelain text, transparent fill). Only watch (amber) and attack (scarlet) fill the pill.

## Typography

**Display Font:** Bahnschrift SemiBold (with embedded Noto Sans Thai SemiBold/Bold for Thai, then Leelawadee UI)
**Body Font:** Segoe UI Variable Text (with Segoe UI, Leelawadee UI)
**Label/Mono Font:** Cascadia Mono (with Consolas) for IPs, URLs, rule IDs

**Character:** DIN-style signage for anything you read at a glance (titles, labels, counts, the clock) over a neutral Windows UI face for sentences. Noto Sans Thai is the signage face's Thai voice because Bahnschrift has no Thai glyphs.

### Hierarchy
- **Display** (600, 24px): page title in the top bar; the login/setup wordmark runs larger. The rail clock uses the figure style at 26px.
- **Headline** (600, 18px): the inspection-line panel heading.
- **Title** (600, 16px): panel headings (H2). Headings carry themselves; nothing sits above them.
- **Figure** (600, 22–24px, tabular numerals): station counts and other headline numbers; tabular figures in data grid cells.
- **Body** (400, 13px): default text, inputs, grid cells.
- **Label** (400, 12–12.5px, Muted): field labels, captions, panel hints; grid headers use the signage face at 12.5px SemiBold in Muted.
- **Mono** (400, 12.5px): IP addresses, URLs, rule IDs.

### Named Rules
**The Signage Rule.** Anything counted or wayfinding (headings, buttons, station names, figures, nav labels in the active state, roundel codes) is set in the signage face at SemiBold. Sentences are set in the body face.

**The Machine Text Rule.** IPs, URLs and rule IDs are always mono so they can be read and compared character by character.

## Layout

A fixed shell: a 256px left rail on Rail Ink (brand block 84px tall, station list, gateway status + local clock + version at the foot) and a content column with an 84px top bar (page title and hint on the left; state pill, avatar ring, user, password and logout small buttons on the right). Content sits in a 28px gutter (28,22,20,0) with a muted provenance footer line beneath. Minimum window 1180×700.

Panels sit on a 16px gap. The dashboard runs: full-width inspection line, then two rows split 5:3 (traffic | threat classes, recent events | top IPs, methods | GeoIP). Panels pad 20×18 (the inspection line 24×20). Panel heading rows take a 14px bottom margin with meta (a tag, a figure, a link) docked right.

The inspection line divides its width into equal columns, one per node; names sit above the track, figures and captions below, so labels centre and wrap without collision at any width.

## Elevation & Depth

Flat. There is one elevation: an Enamel Panel (#111B40) plate on the Midnight Enamel ground, separated by a 1px porcelain edge at 30% opacity (PanelEdge) and nothing else. No drop shadows, no glows, no blur. Notices are a tinted plate (Raised Plate) inside a panel, not a second level. Dropdown popups use the panel fill with an Edge stroke.

### Named Rules
**The Enamel Plate Rule.** Panels are flat enamel with a porcelain edge at 30%. Line Divider (#26346A) is never a panel frame; it is for dividers inside and between regions only.

## Shapes

Small, firm corners: 3px on tags, chips and the checkbox box; 4px on buttons, inputs, dropdowns, notices and the state pill; 6px on panels. Circles are reserved for the transit vocabulary: line roundels (26px in grids, 24px in lists), station rings (24px, 5px stroke), the interchange (34px porcelain disc), nav stops (10px hollow, 18px porcelain with a 4px cobalt stroke when active), and the 36px avatar ring. Termini are 12×34 porcelain bars with 3px corners; pass-through stages are 4×22 ticks. Track and chart strokes run horizontal and vertical; the crossing diagonal in the brand mark is the only angled stroke.

## Components

### Buttons
Sturdy and quiet; one cobalt voice per cluster.
- **Shape:** gently squared (4px), 1px border, min-height 34px, padding 8×16, signage face 13.5px SemiBold.
- **Primary:** Cobalt fill and border, white text. Hover Cobalt Lit; pressed 85% opacity; disabled 40%.
- **Ghost (secondary):** transparent with an Edge outline and porcelain text; hover fills Hover and turns the outline porcelain.
- **Danger:** identical to ghost at rest; under the pointer the outline and text turn Alert Coral. Disabled 35%.
- **Small:** ghost or danger at min-height 26px, padding 3×10, 12.5px, for toolbar and in-row actions. Icons are Segoe Fluent Icons at 13px with a 6px gap.
- **Link:** borderless, Cobalt Signal Text, body face 12.5px, for "ดูทั้งหมด →" and dialog links.
- **Focus:** one 2px Cobalt Signal Text ring offset 3px with 6px corners, on every focusable control.

### Chips
- **Tag:** Line Strong outline, 3px corners, padding 3×8, Muted 12px text; used for time windows and meta.
- **Rule-ID chip:** Line Strong outline, 3px corners, mono 12px in Cobalt Signal Text.

### Cards / Containers
- **Corner Style:** 6px.
- **Background:** Enamel Panel.
- **Shadow Strategy:** none (see Elevation & Depth).
- **Border:** 1px porcelain at 30%.
- **Internal Padding:** 20×18.
- **Notice:** Raised Plate, Line Strong stroke, 4px corners, padding 14×10. **Warn notice:** #2A2614 fill with a #6E5A1A stroke.

### Inputs / Fields
- **Style:** Input Well fill, 1px Line Strong stroke, 4px corners, min-height 34px, padding 2×10, body 13px. Placeholder is carried in a Faint hint line.
- **Hover:** stroke turns Edge.
- **Focus:** stroke turns Cobalt Signal Text at 1.5px (no outer ring on text fields).
- **Disabled:** 50% opacity.
- **Dropdown:** same well and 34px height, a muted chevron, popup in Enamel Panel with an Edge stroke; highlighted item fills Selected, selected item goes SemiBold.
- **Checkbox:** an 18px box with 3px corners, 1.5px Edge stroke on Input Well; checked fills Cobalt with a white tick; hover strokes porcelain.

### Navigation
Each page is a station on a thin vertical line (2px, #2C3A70) in the rail. Rows are 42px: stop marker, Fluent icon (16px), label (13.5px Muted). Hover brightens text to porcelain and strokes the stop porcelain. The current page becomes the interchange: an 18px porcelain stop with a 4px cobalt stroke, SemiBold porcelain label, and an Enamel Panel row fill. No coloured side bar. A Line Divider separates the monitoring group from the administration group.

### Data grids
Transparent ground, no grid lines. Headers in the signage face (12.5px SemiBold, Muted) over an Edge hairline; cells pad 10×9 with tabular figures; each row has a Line Divider bottom rule, Raised Plate on hover and Selected when chosen. Scrollbars are an 8px thumb-only rail (Line Strong, Edge on hover).

### State pill
The glance answer in the top bar. 4px corners, padding 7×12, a 10px dot, the state word in the signage face at 14px over an 11.5px detail line, clickable through to events.
- **Calm (ปกติ):** green outline, porcelain text, green dot, no fill.
- **Watch (มีการบล็อก):** amber fill, midnight text and dot.
- **Attack (กำลังถูกโจมตี):** scarlet fill, white text and dot.
- **Offline:** Alert Coral outline and text, no fill.
The level comes only from real blocked events in the last 5 minutes: attack when there are 10 or more, or one IP has 5 or more; watch when there is any.

### Line roundel
A filled circle in the category's line ink with its two-letter code (SQ, XS, PT, CM, SC…) in the signage face SemiBold at about 42% of the diameter; midnight text on amber, green and slate, white on cobalt and violet. Used in event rows and the threat-class list, where it pairs with a 4px bar in the same ink on a Line Divider track.

### Inspection line (signature)
The dashboard's first panel. An 8px cobalt track runs end to end through: an entry terminus (total requests, gateway host), stop stations (IP / Rate limit, ขนาด / เวลา, Signatures, Anomaly score), a decode tick (muted), the upstream interchange (เว็บจริง), the response-inspection station, and the exit terminus. Each stop is a 24px ring on Enamel Panel: Edge when it has never stopped anything, porcelain when it has, amber when it stopped something in the last 5 minutes, scarlet only while the attack state is live. Figures sit below in the signage face with tabular numerals (Faint at zero, amber or Alert Coral when recent). When a count rises, one ring ripples out (scale 1→2.6, fade 0.9→0 over 900ms, exponential ease-out) in amber, or scarlet if live; skipped when Windows client-area animation is off. Clicking a station opens events filtered to it. The panel refreshes every 5 seconds.

### Coverage rings
OWASP and protection coverage use station rings: full Line Green disc = covered, half amber = partial, hollow slate ring = not covered (outside the WAF's scope). Never red.

## Do's and Don'ts

### Do:
- **Do** keep scarlet (#E21D2D) for the live attack state only: the state pill fill and the stopping station's ring.
- **Do** make primary actions cobalt (#1E5BFF) and keep one primary per action cluster.
- **Do** build destructive actions as Edge-outlined ghosts that turn Alert Coral (#FF7466) only on hover.
- **Do** frame every panel in porcelain at 30% (PanelEdge) on Enamel Panel (#111B40) at a single elevation.
- **Do** give inputs, buttons and dropdowns the same 34px height (26px for the small toolbar variant).
- **Do** colour attack categories only through `Lines.Of`, with the matching two-letter roundel.
- **Do** set IPs, URLs and rule IDs in Cascadia Mono, and counts in the signage face with tabular numerals.
- **Do** draw icons with Segoe Fluent Icons.

### Don't:
- **Don't** use scarlet for buttons, errors, "not covered", offline, or decoration; a permanently red console is a rejected outcome.
- **Don't** add drop shadows, glows or a second elevation level.
- **Don't** use Line Divider (#26346A) as a panel frame; it is for internal dividers only.
- **Don't** put a small uppercase kicker or eyebrow above a panel heading; the heading carries itself.
- **Don't** mark the active nav item with a coloured side bar; the active station is the interchange ring.
- **Don't** prefix status messages with text glyphs (✓ ⚠ ✗); use a Fluent icon or the message colour alone.
