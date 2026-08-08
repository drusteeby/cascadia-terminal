# Cascadia Terminal

An unofficial **Linux port of the Windows Terminal experience**, built in .NET.

Windows Terminal's own codebase is welded to Windows (WinUI/XAML, DirectX,
ConPTY), so this port rebuilds the app around a Linux-capable engine while
porting the parts of [microsoft/terminal](https://github.com/microsoft/terminal)
that travel: the settings.json format, the color schemes, the keybinding
surface, and the tab/pane interaction model.

## Architecture

```
cascadia-terminal  (this repo)
├── src/CascadiaTerminal        Avalonia host + Windows Terminal settings engine
│   └── Settings/               settings.json parser, scheme converter, profile mapper
├── assets/                     schemes/actions/keybindings extracted from microsoft/terminal
└── tests/CascadiaTerminal.Tests

../RoyalTerminal  (local fork, branch linux-port-fork)
└── terminal engine + reusable shell (tabs, panes, search), extended by this
    project with mouse-centric UX and host-injection points
```

The terminal engine is a local fork of
[RoyalTerminal](https://github.com/royalapplications/RoyalTerminal)
(MIT), referenced via `ProjectReference` so engine fixes land in the fork
directly. VT processing uses Ghostty's `libghostty-vt`.

## Windows Terminal features ported

- **Tabs**: reorder by drag, double-click rename (sticky over shell titles),
  middle-click close, duplicate (with working directory), close others/right,
  tab context menu, `Ctrl+1..9` switching, tab title follows the shell (OSC)
- **Panes per tab**: split right/down/auto, drag splitters with the mouse,
  focus/resize with keyboard, zoom a pane (`togglePaneZoom`), close pane or
  tab with one key
- **Mouse copy/paste**: selection (char/word/line, block with Alt),
  copy-on-select option, `Ctrl+Shift+C/V`, right-click context menu,
  middle-click paste, Ctrl+click to open URLs (OSC 8 and plain text)
- **settings.json**: the Windows Terminal format (JSONC), including
  `profiles.defaults`/`profiles.list`, `schemes`, `defaultProfile`,
  `copyOnSelect`, `initialCols/Rows`, nested `font` objects, and the legacy
  flat profile array — generated on first run with your installed shells
- **Color schemes**: all 16 schemes shipped by Windows Terminal (Campbell,
  One Half Dark/Light, Solarized, Tango, Vintage, Dimidium, …) plus user
  schemes from settings.json
- **Search**: `Ctrl+Shift+F` find-in-buffer with highlights

## Default keybindings (Windows Terminal parity)

The defaults are Windows Terminal's own (ported from its defaults.json), so
chords like `Ctrl+T`/`Ctrl+W` stay free for the shell (readline). Rebind
anything through the `actions`/`keybindings` arrays in settings.json.

| Action | Keys |
|---|---|
| New tab | `Ctrl+Shift+T` |
| New window | `Ctrl+Shift+N` |
| Close pane (or tab when last pane) | `Ctrl+Shift+W` |
| Duplicate tab | `Ctrl+Shift+D` |
| Next / previous tab | `Ctrl+Tab` / `Ctrl+Shift+Tab`, `Ctrl+PgDn/PgUp` |
| Split pane right / down (with cwd) | `Alt+Shift+Plus` / `Alt+Shift+Minus` |
| Split pane auto | `Alt+Shift+D` |
| Move pane focus | `Alt+Arrows` |
| Resize pane | `Alt+Shift+Arrows` |
| Zoom pane | `Ctrl+Shift+X` |
| Copy / paste | `Ctrl+Shift+C` / `Ctrl+Shift+V` |
| Find | `Ctrl+Shift+F` |
| Select all | `Ctrl+Shift+A` |
| Font size | `Ctrl+Plus` / `Ctrl+Minus` / `Ctrl+0` |

## Settings

`~/.config/cascadia-terminal/settings.json` — Windows Terminal's format,
and the **single source of truth**. Comments and trailing commas are
allowed. Profiles map onto PTY sessions; `commandline`, `startingDirectory`
(`~`, `$HOME`, and `%USERPROFILE%` all work), per-profile `colorScheme`,
`fontFace`/`fontSize`, `historySize`, `environment`, and `hidden` are
honored. `actions`/`keybindings` rebind keys using Windows Terminal's chord
syntax (`"keys": "ctrl+shift+o"`), including `"unbound"`. Missing fonts fall
back through Cascadia Mono → JetBrains Mono → Fira Code → Ubuntu Mono →
DejaVu Sans Mono → monospace.

- **Hot reload**: external edits to settings.json re-apply to open windows
  (profiles and keybindings) without a restart.
- **Round-trip**: profile edits made in the in-app settings panel are
  written back into settings.json (unknown fields preserved). Profiles the
  WT format can't express (e.g. SSH) persist under
  `cascadiaExtendedProfiles` in the same file.

## Build & run

Requires the .NET 10 SDK and the RoyalTerminal fork checked out as a sibling
directory (`../RoyalTerminal`).

```sh
dotnet build src/CascadiaTerminal/CascadiaTerminal.csproj -c Release
dotnet src/CascadiaTerminal/bin/Release/net10.0/CascadiaTerminal.dll
```

Tests:

```sh
dotnet test tests/CascadiaTerminal.Tests
```

## License

MIT. Portions derived from microsoft/terminal (MIT) and
royalapplications/RoyalTerminal (MIT).
