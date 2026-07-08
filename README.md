# NoFences

Didn't want to pay 11€, made my own.

![Screenshot](screenshot.png "NoFences in action")

## Features

- **Fences**: Drag files and folders from the desktop into resizable, translucent fences. Double-click an item to open it.
- **Roll-up**: Enable "Minify" and a fence collapses to its title bar until you hover over it — great for keeping game launchers out of sight.
- **Fence colors**: Give each fence its own tint (right-click → *Fence color…*) to organize by topic, e.g. one color per class or project.
- **Quick-hide**: Press **Ctrl+Alt+H** (or double-click the tray icon) to hide/show all fences at once for a distraction-free desktop.
- **Folder portals**: Right-click → *Folder portal…* to mirror a real folder (e.g. Downloads or a cloud-synced folder) live inside a fence. Dropping files onto a portal moves them into that folder.
- **Auto-sort rules**: Right-click → *Auto-sort rules…* and enter wildcard patterns like `*.png; screenshot*`. New files appearing on the desktop that match are added to the fence automatically. *Sort desktop now* applies the rules to existing desktop files.
- **Icon sizes**: Choose small (32), medium (48) or large (64) icons per fence for easier viewing.
- **Tray icon**: Create fences, toggle quick-hide, run the sort rules or exit — all from the notification area.
- **Hide fenced items on desktop** (tray option): Files inside a fence get the Hidden attribute so Explorer no longer shows them on the desktop — no more duplicate icons. Removing an item from its fence (or deleting the fence) makes it visible again.
- **Start with Windows** (tray option): Toggles an autostart entry for the current user.
- Rounded corners and background blur on Windows 11.

Fence layout and settings are stored per fence in `%LOCALAPPDATA%\NoFences`.
