# FlowGrid

<img width="1919" height="1013" alt="image" src="https://github.com/user-attachments/assets/11d05179-8c98-4763-95dc-5e132cb8ccef" />

## Features

- **Fences**: Drag files and folders from the desktop into resizable, translucent fences. Double-click an item to open it.
- **Roll-up**: Enable "Minify" and a fence collapses to its title bar until you hover over it — great for keeping game launchers out of sight.
- **Fence colors**: Give each fence its own tint (right-click → *Fence color…*) to organize by topic, e.g. one color per class or project.
- **Quick-hide**: Press **Ctrl+Alt+H** (or double-click the tray icon) to hide/show all fences at once for a distraction-free desktop.
- **Folder portals**: Right-click → *Folder portal…* to mirror a real folder (e.g. Downloads or a cloud-synced folder) live inside a fence. Dropping files onto a portal moves them into that folder.
- **Auto-sort rules**: Right-click → *Auto-sort rules…* and enter wildcard patterns like `*.png; screenshot*`. New files appearing on the desktop that match are added to the fence automatically. *Sort desktop now* applies the rules to existing desktop files.
- **Icon sizes**: Choose small (32), medium (48) or large (64) icons per fence for easier viewing.
- **Search**: Right-click → *Search in fence…* opens a filter box in the title bar; typing narrows the visible items (Esc closes).
- **Scrollbar**: Fences with more content than fits get a proportional, draggable scrollbar (mouse wheel works too).
- **Sorting**: Per fence, display items manually or sorted by name, type or date (right-click → *Sort items*).
- **Background opacity**: Per fence, choose how strongly the background tint shows (right-click → *Background opacity*).
- **Move items between fences**: Drag an icon from one fence and drop it into another; the desktop-hidden state moves along.
- **Snapping**: When you finish moving a fence, its edges snap to nearby fences and the screen edges.
- **New-item highlight**: Freshly added items glow golden for a few seconds so you can see what the auto-sort rules did.
- **Portal navigation**: Double-click a folder inside a portal to browse into it; the ".." tile goes back up.
- **Layout backup**: Export/import all fences as a single XML file from the tray menu.
- **Tabs**: Split a fence into tabs (right-click → *Tabs* → *Add tab…*), e.g. "Steam | Emulators | VR | Launchers". Click to switch, drag an item onto a tab header to move it there, "+" in the strip adds another tab.
- **Sticky notes**: Tray → *New widget* → *Sticky note* creates a fence you can type into; the text saves automatically.
- **Widgets**: Tray → *New widget* → *Clock*, *CPU & RAM* or *Calendar* — small always-on-desktop panels sharing all fence styling (color, opacity, lock, snapping).
- **Animations**: Fences fade in on startup, scale down subtly while being dragged and bounce back on release; hovered items get a soft glow.
- **Tray icon**: Create fences, toggle quick-hide, run the sort rules or exit — all from the notification area.
- **Hide fenced items on desktop** (tray option): Files inside a fence get the Hidden attribute so Explorer no longer shows them on the desktop — no more duplicate icons. Removing an item from its fence (or deleting the fence) makes it visible again.
- **Start with Windows** (tray option): Toggles an autostart entry for the current user.
- Rounded corners and background blur on Windows 11.

## Widget plugins

FlowGrid loads custom widgets from DLLs in `%LOCALAPPDATA%\FlowGrid\Plugins` (tray → *New widget* → *Open plugins folder*). To build one:

1. Create a .NET Framework 4.8 class library and reference `FlowGrid.Sdk.dll`.
2. Implement `FlowGrid.Sdk.IFlowGridWidget`:

```csharp
public class MyWidget : IFlowGridWidget
{
    public string Name => "My widget";          // shown in the "New widget" menu
    public int RefreshIntervalMs => 1000;       // 0 = static, no auto-redraw

    public void Render(Graphics g, Rectangle area, IWidgetHost host)
    {
        g.DrawString("Hello!", host.BaseFont, Brushes.White, area);
    }
}
```

3. Copy the DLL into the plugins folder and restart FlowGrid — the widget appears under *New widget*.

**SDK v2**: implement `IFlowGridWidget2` instead to also receive mouse clicks, and use `host.Settings` (a free-form string persisted with the fence) to store per-fence state:

```csharp
public bool OnClick(Point location, Rectangle area, IWidgetHost host)
{
    host.Settings = "clicked=1";   // saved immediately, per fence
    return true;                   // true = repaint now
}
```

**SDK v3**: implement `IFlowGridWidget3` to contribute entries to the fence's right-click menu, ask the user for text input and request repaints from background threads:

```csharp
public IList<WidgetMenuItem> GetMenuItems(IWidgetHost host)
{
    return new List<WidgetMenuItem>
    {
        new WidgetMenuItem("Configure...", () =>
        {
            var input = host.PromptText("Configure", "Enter a value:", "");
            if (input != null) host.Settings = input;
        })
    };
}
// ...and after an async fetch completes:
host.RequestRefresh();   // safe from any thread
```

**SDK v4**: implement `IFlowGridControlWidget` to host a **real WinForms control** inside the fence — tables (`DataGridView`), forms, WebView2, anything. `CreateControl` is called once per fence, so instances stay independent. For API integrations, `host.GetSecret`/`host.SetSecret` store tokens **DPAPI-encrypted** (bound to the Windows user, scoped per widget type, never part of layout exports):

```csharp
public class MyEnterpriseWidget : IFlowGridControlWidget
{
    public Control CreateControl(IWidgetHost host)
    {
        var token = host.GetSecret("api-token");   // null until configured
        return new MyDashboardControl(host);       // any WinForms control
    }
}
```

Control hosting rules: **the host owns layout** — do not set `Dock` on your
root control (it is reset), the fence positions and resizes it and keeps a
small frame so the fence stays movable/resizable. Right-click shows the
fence menu (including your `GetMenuItems` entries) unless a control brings
its own `ContextMenuStrip`.

The `SampleWidgets` project contains working examples: system uptime, weather (open-meteo, keyless, location via context menu), a **system monitor** (CPU/RAM/GPU/VRAM/disks with clickable toggle chips) and a **stock/ETF watchlist** (Yahoo Finance, keyless): per-fence symbol lists managed via the "+" button and the context menu, clickable rows opening a detail page with a one-month price chart. The **Jira widget** is the enterprise blueprint: configure URL/e-mail/API token via the context menu (token stored encrypted), your open issues appear in a dark-styled table, double-click opens the issue in the browser. Plugin exceptions are caught and shown inside the fence, so a broken widget cannot crash the app. **Plugins run with full trust — only install DLLs you wrote or trust.**

Fence layout and settings are stored per fence in `%LOCALAPPDATA%\FlowGrid`. On first start, data from a previous NoFences installation (`%LOCALAPPDATA%\NoFences`) is migrated automatically — close the old app before launching FlowGrid so the move succeeds.
