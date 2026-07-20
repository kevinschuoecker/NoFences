# ADR 0001: Crash handling, logging and atomic persistence

Status: accepted (Sprint 1)

## Problem

Unhandled exceptions killed the process silently, bugs in the field were
undiagnosable (no logs), and fence metadata was written non-atomically —
a crash mid-write corrupts the user's layout, which is the single worst
trust-breaker for a desktop organizer.

## Considered alternatives

1. Third-party logging (NLog/Serilog) + crash reporter (Sentry).
2. Minimal in-house logger + global WinForms handlers + atomic file swap.
3. Do nothing until after release.

## Decision

Option 2.

- `Util/Log`: thread-safe daily files under `%LOCALAPPDATA%\FlowGrid\Logs`,
  14-day retention, never throws.
- Global handlers: `Application.ThreadException` (dialog, may continue),
  `AppDomain.UnhandledException` (dialog, exit), `UnobservedTaskException`
  (log only). `CrashDialog` is storm-guarded (3+ crashes/minute → log only).
- `Util/SafeStorage`: serialize to `.tmp`, `File.Replace` into place keeping
  `.bak`; reads fall back to `.bak`; unreadable files preserved as
  `.corrupt-*` for diagnosis.

## Rationale

Zero new dependencies (plugin DLL deployment stays trivial, no version
conflicts with plugins that may bring their own logger), ~200 lines we
fully own, covers 100 % of the current diagnostic need. Sentry-style
telemetry needs consent/privacy work — post-release.

## Downsides

No structured logging, no remote crash aggregation. Acceptable at this
stage; the file format is greppable and users can attach logs to reports.

## Impact on plugins/API

None yet. A `host.Log(...)` forwarding into the same file is planned (P1)
and will be additive.

## Breaking changes

None. Existing metadata files load unchanged; `.bak`/`.tmp` siblings are
new but ignored by older builds.
