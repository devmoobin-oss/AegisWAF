---
version: 1
slug: "src-aegiswaf-desktop-mainwindow-xaml"
primary_target: "src/AegisWAF.Desktop/MainWindow.xaml"
related_targets: ["src/AegisWAF.Desktop/Theme.xaml","src/AegisWAF.Desktop/Views"]
---

# Surface brief — AegisWAF desktop console (WPF)

Scope: the whole native Windows app — setup wizard, login, change-password/prompt dialogs, main shell and its nine pages. Mode: Operate. Audience: SOC analysts who keep it open all day; the glance question is "are we under attack right now". Must not feel neon/hacker, generic SaaS, pretty-but-unreadable, or permanently red. All behaviour, permissions, test-backed claims and real-data rules stay.

## Direction contract

THESIS: The WAF is a line every request rides: it enters at the gateway address, passes the inspection stations, and either alights where it was stopped or reaches the real site. The console shows that line live. It refuses the category default of a KPI-card grid on a neon-black console.

OWN-WORLD: Midnight enamel ground #0B1230, panels #111B40, porcelain ink #F4F3EE, porcelain-stroked panel edges, four line inks (cobalt #1E5BFF, amber #FFC20E, green #22B14C, violet #8A6BD8) as category and series colours. Scarlet #E21D2D is held back for one job only: the live "under attack" state (translation: the board's scarlet primary button becomes cobalt, because the user ruled out constant red). Bahnschrift (DIN, the signage face) for labels, numbers and tracked uppercase section heads; Leelawadee UI / Segoe UI for Thai and body; Cascadia Mono/Consolas for IPs and URLs. Station rings, line roundels, 45/90 angles only.

STORY: The analyst glances and reads the state word and the line; if a station is red they see which stage is stopping what, open the event, and block the IP or adjust the rule in seconds.

FIRST VIEWPORT: Left rail (brand, nine nav items marked as stations on a thin line, system status + local clock at the foot). Top bar: page title, live state pill (ปกติ / มีการบล็อก / กำลังถูกโจมตี), user, actions. Dashboard row 1, full width: the inspection line — origin station "ผู้ใช้ → gateway URL", stations IP/Rate limit, รูปแบบ/ขนาด, Signatures, Anomaly, Upstream (ถึงเว็บ), Response, terminus = upstream site, each with its real count; self-test and copy/open URL live in its header. Row 2: 24h traffic (cobalt requests, amber blocked) | threat classes on a fixed magnitude ramp in their line inks. Row 3: recent events with line roundels | top attacking IPs.

FORM: Midnight Transit Diagram — dealt challenger (catalog wayfinding-cartography-signage-midnight-transit-diagram), chosen by the user over the assigned #6 (Munich ’72 Wayfinding). seed:d37bbe43f70e3b41. Code-led. Signature interaction: the inspection line — station counts update every 5 s; the stopping station's ring turns scarlet only while the attack state is live, and clicking a station opens the events filtered to it.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
