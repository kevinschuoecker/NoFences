# FlowGrid release checklist

Copy this list into the release issue/PR and tick every box. A release is
tagged only when all P0 items pass; P1 items may be waived with a written
reason.

## Build & automation (P0)

- [ ] `main` is green in CI (build + tests)
- [ ] Version number decided (SemVer), matches planned tag `vX.Y.Z`
- [ ] `CHANGELOG.md` contains a `## [X.Y.Z]` section (pipeline fails without it)
- [ ] Release notes read like they were written for users, not commits

## Functional smoke test on a clean machine/VM (P0)

- [ ] Installer: fresh install, per-user, no admin prompt, app starts
- [ ] Installer: autostart option creates the Run entry; uninstall removes it
- [ ] Upgrade: install over previous version - fences, settings, secrets and
      plugins survive
- [ ] Uninstall: program directory removed, user data under
      `%LOCALAPPDATA%\FlowGrid` intentionally preserved
- [ ] Portable ZIP: extract anywhere, runs, README-portable.txt correct
- [ ] Data compatibility: layout from the previous release loads unchanged
      (copy an old `%LOCALAPPDATA%\FlowGrid` before testing)
- [ ] Plugin compatibility: plugins built against the previous SDK still load;
      incompatible ones fail with a log entry, not a crash

## Core scenarios (P0)

- [ ] Create fence, drag items in, double-click opens, remove from fence
- [ ] Roll-up (minify) + animation, quick-hide hotkey, tray menu complete
- [ ] Folder portal on Downloads: live refresh, navigation, drop-to-move
- [ ] Auto-sort rule fires for a new desktop file
- [ ] Each built-in widget renders; each sample widget renders
- [ ] Crash handling: force an error (corrupt a fence XML) - app starts,
      logs it, fence is skipped, `.corrupt-*` file preserved

## Environments (P1)

- [ ] Windows 11 (primary)
- [ ] Windows 10 22H2
- [ ] High-DPI (150 %/200 %) - text and hit targets usable
- [ ] Multi-monitor - fences stay on their monitor, snapping works
- [ ] Dark and light system theme

## Legal & docs (P0)

- [ ] `LICENSE` and `THIRD-PARTY-NOTICES.md` shipped in installer and ZIP
- [ ] No "Fences" (Stardock) wording in product UI, installer or store text
- [ ] README quickstart matches the released behavior

## Publishing (P0)

- [ ] Tag pushed, pipeline green, GitHub release created automatically
- [ ] Installer + portable ZIP + SHA256SUMS attached
- [ ] Checksums verified once manually after download
- [ ] Post-release: install from the published artifact (not a local build)
