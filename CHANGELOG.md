# Changelog

All notable changes to FlowGrid are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com); versions follow SemVer.
The release pipeline publishes the section matching the pushed tag.

## [Unreleased]

## [1.0.0]

First FlowGrid release (formerly NoFences).

### Added
- Fence colors, per-fence background opacity, icon sizes (32/48/64)
- Tabs inside fences with per-tab item lists and drag-over switching
- Item sorting (manual/name/type/date) and in-fence search
- Folder portals with live refresh, in-fence navigation and drop-to-move
- Auto-sort rules (wildcards) applied to new desktop files
- Quick-hide (Ctrl+Alt+H), tray icon, autostart toggle
- Hide-fenced-items-on-desktop mode
- Sticky notes and built-in widgets (clock, CPU/RAM, calendar)
- Widget plugin platform (SDK v4): rendering, clicks, context menu
  contributions, input dialogs, per-fence settings, DPAPI secrets and
  WinForms control hosting
- Sample widgets: weather, stock/ETF watchlist with charts, system
  monitor with toggles, Jira issue table, uptime
- Layout export/import, fence-to-fence item drag, edge snapping,
  animations (fade-in, drag scale/bounce, hover glow, smooth roll-up)
- Crash-safe storage (atomic writes + backups), global crash handling,
  file logging, per-user installer, automated release pipeline

### Changed
- Project renamed from NoFences to FlowGrid; existing data migrates
  automatically on first start
