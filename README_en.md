# AoE4 Overlay CS

`AoE4 Overlay CS` is a desktop overlay tool for **Age of Empires IV**, built with `C# + WPF`.

Its main goals are:

- Bind a target player by name or profile ID
- Pull the latest game and match history from `aoe4world.com`
- Display match information through an in-game overlay
- Expose a local WebSocket data channel for HTML overlay or external viewers
- Support Chinese/English UI switching, localized map names, hotkey toggle, drag/resize overlay positioning, and searchable ID history

Chinese documentation: [`README.md`](./README.md)

## Screenshots

<img width="1149" height="236" alt="overlay-preview-1" src="https://github.com/user-attachments/assets/e24325f7-cda0-432e-bdf5-1dbd8ce83d10" />
<img width="972" height="245" alt="overlay-preview-2" src="https://github.com/user-attachments/assets/9be65e8d-ebb0-4341-8469-6cccc6c20b45" />
<img width="836" height="624" alt="main-window-preview" src="https://github.com/user-attachments/assets/4afb1df9-bad1-47e1-a2f9-4d3a05ae3faf" />

## Version

- Current version: `1.7.1`
- Target frameworks: `net8.0-windows;net10.0-windows`

## Project Purpose

This project is not a game mod or an injected plugin. It is a standalone Windows desktop tool.

It uses public API data plus a transparent topmost window and a local HTML/WebSocket output pipeline to render match overlays.

Typical use cases:

- Personal match lookup for the latest game
- Stream / recording overlay display
- Custom HTML overlay styling and debugging
- Local development using the WebSocket payload as a reusable data source

## Technical Architecture

### High-Level Structure

```text
AoE4OverlayCS (WPF Desktop App)
├─ UI Layer
│  ├─ MainWindow
│  ├─ SettingsView
│  ├─ GamesView
│  └─ OverlayWindow
├─ ViewModel Layer
│  └─ MainViewModel
├─ Service Layer
│  ├─ ApiCheckerService
│  ├─ GameProcessor
│  ├─ WebSocketServerService
│  ├─ SettingsService
│  ├─ GlobalHotkeyService
│  ├─ MapNameTranslator
│  ├─ CivIconResolver
│  ├─ WindowServices
│  └─ LogPaths
├─ Model Layer
│  └─ AppSettings
└─ Static Assets
   ├─ html/
   ├─ img/
   └─ Resources/
```

### Data Flow

```text
User enters player name / profile ID
  -> MainViewModel.SearchPlayer()
  -> ApiCheckerService calls aoe4world API
  -> SettingsService persists player binding
  -> RefreshHistory() updates match history
  -> GetLastGame() pulls latest game
  -> GameProcessor transforms match data
  -> WebSocketServerService broadcasts player_data
  -> OverlayWindow updates immediately
```

### Display Pipeline

```text
aoe4world API
  -> ApiCheckerService
  -> GameProcessor
  -> OverlayWindow (native WPF overlay)
  -> WebSocketServerService
  -> html/overlay.html + main.js (HTML overlay)
```

## Tech Stack

- Desktop UI: `WPF`
- Language: `C#`
- Frameworks: `.NET 8 / .NET 10`
- JSON: `Newtonsoft.Json`
- WebSocket server: `Fleck`
- Global hotkeys: `NHotkey.Wpf`
- Fallback keyboard handling: `Win32 Low-Level Keyboard Hook`
- Logging dependencies: `Serilog`, `Serilog.Sinks.File`
- Image handling: `SixLabors.ImageSharp`
- HTML overlay: `HTML + CSS + JavaScript + jQuery`

## Core Modules

### 1. Main Window and Views

- `MainWindow.xaml`: main application shell, menu, tabs, tray integration, language switching
- `SettingsView.xaml`: player search, search history, hotkey configuration, font size, team gap settings
- `GamesView.xaml`: recent match history
- `OverlayWindow.xaml`: transparent in-game overlay with lock/unlock behavior and live updates

### 2. MainViewModel

`MainViewModel` acts as the main coordinator for:

- player search
- binding persistence
- history refresh
- overlay refresh
- WebSocket startup/shutdown
- localized map refresh after language change
- search history management

### 3. API Layer

`ApiCheckerService` communicates with `aoe4world.com` and handles:

- player search by name or profile ID
- fetching the latest game
- fetching recent match history
- polling for new matches

### 4. Data Processing Layer

`GameProcessor` transforms raw match JSON into overlay-ready data, including:

- map name
- queue / mode / server info
- team ordering
- rank, rating, win rate, wins, losses
- civilization data
- country data

### 5. Map Localization Layer

`MapNameTranslator` localizes map names based on UI language:

- Chinese UI: map names are translated to Chinese
- English UI: original English names are preserved
- This applies to:
  - overlay map names
  - match history map names

### 6. Overlay Layer

`OverlayWindow` provides:

- transparent topmost rendering
- click-through mode when locked
- drag/resize when unlocked
- `30%` background opacity when locked
- immediate map localization updates
- civilization and country icon loading with caching

### 7. WebSocket Output Layer

`WebSocketServerService` runs a local WebSocket server on a configurable port.

It is used to:

- broadcast `player_data`
- drive `html/overlay.html`
- provide a reusable data source for custom frontends or stream integrations

## Feature List

### Player Search and Binding

- Search by player name or profile ID
- Search via button click
- Search via `Enter` key in the input box
- Show validation message on empty input
- Automatically refresh latest game and history after successful search

### Search History

- Every successful search is recorded
- Clicking the input box shows previous search records
- Old records can be selected directly
- Any single history item can be deleted
- Search history is persisted to config

### Overlay Display

- Show latest match map information
- Show both teams and players
- Show civilization, country, rating, rank, win rate, wins and losses
- Team-colored background for player names
- Support localized map display

### Overlay Control

- Global hotkey to show / hide overlay
- Automatic fallback to low-level keyboard hook if system hotkey registration fails
- Unlock mode supports drag and resize
- Lock mode supports click-through behavior

### Match History

- Display recent match history
- Show both team rosters
- Show current bound player's result and rating diff
- Chinese UI displays localized map names
- English UI preserves original English names

### Multi-Language Support

- Supports `Chinese / English` UI switching
- Language preference is persisted
- Previous language is restored on next startup
- Switching language refreshes both overlay and history map names immediately

### Local HTML Overlay

- Includes `html/overlay.html`
- Receives live `player_data` via WebSocket
- Can be customized through HTML / CSS / JavaScript

### Application Behavior

- Single-instance startup
- Closing the main window minimizes to tray instead of exiting
- Tray menu supports open and exit
- Supports opening the HTML folder and log folder from the app menu

## Directory Structure

```text
AoE4_Overlay_CS/
├─ AoE4OverlayCS.csproj
├─ App.xaml
├─ App.xaml.cs
├─ MainWindow.xaml
├─ MainWindow.xaml.cs
├─ Models/
│  └─ AppSettings.cs
├─ ViewModels/
│  ├─ MainViewModel.cs
│  └─ RelayCommand.cs
├─ Services/
│  ├─ ApiCheckerService.cs
│  ├─ CivIconResolver.cs
│  ├─ GameProcessor.cs
│  ├─ GlobalHotkeyService.cs
│  ├─ LogPaths.cs
│  ├─ MapNameTranslator.cs
│  ├─ SettingsService.cs
│  ├─ WebSocketServerService.cs
│  └─ WindowServices.cs
├─ Views/
│  ├─ GamesView.xaml
│  ├─ OverlayWindow.xaml
│  ├─ OverrideView.xaml
│  └─ SettingsView.xaml
├─ Resources/
│  ├─ Strings.zh-CN.xaml
│  └─ Strings.en-US.xaml
├─ html/
│  ├─ overlay.html
│  ├─ main.js
│  ├─ main.css
│  ├─ custom.js
│  └─ custom.css
└─ img/
   ├─ flags/
   ├─ maps/
   └─ ...
```

## Build and Run

### Requirements

- Windows
- .NET SDK `10.0.103` or a compatible SDK resolved by `global.json`

### Development Run

```bash
dotnet build "AoE4OverlayCS.csproj"
dotnet run --project "AoE4OverlayCS.csproj"
```

### Run Executable Directly

After building:

```text
AoE4OverlayCS.exe
```

### Notes

The repository includes `global.json` to pin the SDK and keep `net10.0-windows` builds consistent.

## Config and Logs

### Config File

The current implementation stores configuration in the runtime directory:

```text
config/config.json
```

Stored settings include:

- bound player info
- selected language
- overlay hotkey
- overlay geometry
- font size
- team gap
- auto-open overlay setting
- search history

### Common Logs

Typical log files include:

- `hotkey.log`
- `dispatcher_error.log`
- `domain_error.log`
- `tray_error.log`
- `image_load_error.log`

## Localization Notes

The current project already supports:

- Chinese and English UI switching
- bilingual map name translation
- immediate overlay map refresh after language change
- immediate match history map refresh after language change

## Project Characteristics

### Strengths

- simple single-project structure
- dual output paths: native WPF overlay and HTML overlay
- complete loop from search -> history -> overlay -> hotkey
- localization is already integrated into the actual data flow

### Good Areas for Future Expansion

- add a dedicated test project
- unify logging through a single pipeline
- expand map translations and icon assets
- expose more match fields and UI customization
- allow richer WebSocket consumer integrations

## Open Source Dependencies

- `Fleck`
- `Newtonsoft.Json`
- `NHotkey.Wpf`
- `Serilog`
- `Serilog.Sinks.File`
- `SixLabors.ImageSharp`

## Project Link

- GitHub: `https://github.com/gearlam/AoE4_Overlay_CS`

## Suggested Reading Order

If you want to understand or extend the project quickly, start from:

1. `MainWindow.xaml.cs`
2. `ViewModels/MainViewModel.cs`
3. `Services/ApiCheckerService.cs`
4. `Services/GameProcessor.cs`
5. `Views/OverlayWindow.xaml.cs`

That gives you the fastest path through the main execution flow.
