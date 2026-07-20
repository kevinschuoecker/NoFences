# Documents still missing for a public release

Status list only - deliberately not written yet. Order = suggested priority.

## Before the public beta (P0)

1. **User quickstart** - rewrite README top section for end users (install,
   first fence, quick-hide, portals) with screenshots. Current README mixes
   user and developer content.
2. **GitHub issue templates** - bug report (with log-attachment instruction)
   and feature request.
3. **Beta invitation text** - what FlowGrid is, what to test, how to report.

## Before the paid release (P0)

4. **EULA** - required once money changes hands; must reference the MIT
   heritage and third-party notices. Needs legal review, not homemade.
5. **Privacy policy** - even though FlowGrid sends no telemetry today, the
   widgets call third-party APIs (open-meteo, Yahoo, ipapi.co, Jira). One
   page stating what leaves the machine and when.
6. **Download/landing page** - product page with screenshots, system
   requirements (Windows 10 1809+, .NET Framework 4.8), pricing, checksums,
   changelog link.
7. **FAQ** - install/SmartScreen, where data lives, how to uninstall, how
   plugins work, "is this a Fences clone?" answer (carefully worded).

## Soon after release (P1)

8. **Plugin developer guide** - move the README SDK section into
   docs/plugin-dev.md with a full 30-minute tutorial (Sprint 2B input).
9. **Support policy** - supported Windows versions, how long betas live,
   security-issue contact.
10. **Press kit** - logo, screenshots, one-paragraph description.

## Later (P2)

11. Website imprint/legal pages depending on jurisdiction (AT/EU: Impressum,
    withdrawal right if sold directly).
12. Localized UI strings + localized docs (German first - the UI is
    currently English-only, decide direction before translating docs).
