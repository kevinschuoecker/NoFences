# FlowGrid beta test plan

Goal: an external tester downloads, installs and uses FlowGrid productively
for two weeks without any help. Every session that needs our support is a
finding.

## Setup

- 5-10 testers, mixed: Windows 10 22H2 and Windows 11, at least one
  high-DPI laptop (150 %+), at least one multi-monitor setup, at least one
  non-admin user account.
- Distribution: GitHub pre-release (e.g. v0.9.x) with installer, portable
  ZIP and checksums. No personal hand-holding - the download page and the
  welcome note are the only onboarding.
- Feedback channel: GitHub issues (template with "attach the newest file
  from %LOCALAPPDATA%\FlowGrid\Logs").

## Phase 1 - Installation (day 1)

| Scenario | Expected |
|---|---|
| Fresh install, standard user, no admin | No UAC prompt, app starts, welcome note + empty fence appear |
| Install with autostart option | Run entry exists; reboot starts FlowGrid |
| Portable ZIP on a second machine | Runs from any folder; data lands in %LOCALAPPDATA%\FlowGrid |
| Upgrade over previous beta | Fences, settings, secrets, plugins survive |
| Uninstall | Program folder gone, user data preserved, autostart entry gone |
| SmartScreen behavior | Document what testers see (unsigned builds will warn - collect impact) |

## Phase 2 - Daily use (week 1)

Testers use their real desktop. Ask them to try at least once:

- 3+ fences with items, colors, opacity, icon sizes
- One fence with tabs, one folder portal (Downloads), one auto-sort rule
- Quick-hide during real work; roll-up on at least one fence
- One widget (clock/weather/stocks) and one sticky note
- Layout export, then import
- Explorer restart (Task-Manager -> Restart Explorer): fences must survive
- Sleep/resume and monitor unplug/replug: fences must stay reachable

## Phase 3 - Abuse (week 2)

- 200+ items in one fence (scrolling, search, CPU while hovering)
- Portal on a huge folder and on a network share
- Kill FlowGrid via Task-Manager mid-drag, restart: no data loss (.bak path)
- Corrupt a fence XML on purpose: app starts, fence skipped, .corrupt-* kept
- Drop a random/broken DLL into Plugins: app starts, error logged
- Run 48h continuously: Task-Manager private bytes and GDI objects at start
  vs. end (GDI must stay well below 2,000)

## Exit criteria for the beta

- 0 crashes without a CrashDialog + log entry
- 0 data-loss reports
- 0 "fence unreachable" reports (off-screen, behind windows, gone)
- Installation success without help: 100 % of testers
- CPU in idle < 1 %, while hovering a fence < 10 % on a mid-range machine

## Known observations to collect (not fixed yet, by design)

- Windows 10: fence dragging may stutter (OS-level blur/drag issue) - collect
  frequency and hardware, decide on a "reduce effects" setting afterwards
- DPI change while running: fence sizes don't rescale until restart
- Icon cache growth after extensive portal browsing (restart clears)
