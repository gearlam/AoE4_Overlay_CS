# AoE Overlay

`AoE Overlay` is a desktop overlay for **Age of Empires IV** and **Age of Empires II: Definitive Edition**, built with `C# + WPF`. The program file is `AoEOverlay.exe`.

Its main goals are:

- Bind a target player by name or profile ID
- Pull the latest game and match history from `aoe4world.com`
- Display match information through an in-game overlay
- Expose a local WebSocket data channel for HTML overlay or external viewers
- Support Chinese/English UI switching, localized map names, hotkey toggle, drag/resize overlay positioning, and searchable ID history
- 2.1.1: optional Shift monitoring for the infinite macro loop. Shift is checked only during the last second of the interval; if held, the combo waits and fires immediately on release
- 2.2.1: switch polling between AOE4 and AOE2 on the settings page. AOE2 uses the official community API, lists ranked matches, and translates map names from the game's own Chinese strings

Chinese documentation: [`README.md`](./README.md)

## Overlay DLC civilization update

- Added **Vikings** and **Scots** from *Raiders of the North*, bringing local AOE4 civilization flags to 25.
- Both WPF and HTML overlays support the new flags, with localized civilization names and flag labels.
- Fixed local icon resolution for underscore-separated API names; English names use canonical spelling. Match history shares the same translation table.
- Asset and name sources: [`img/flags/SOURCES.md`](./img/flags/SOURCES.md).

## 3.0.0: In-game chat translation

Open **Settings → Game chat translation settings**:

1. Enter an OpenAI-compatible base URL (including `/v1`, or a full `/chat/completions` URL), API key and model name.
2. Select **Direct (default) / System / Custom** network mode. If Cherry Studio works but this app does not, match its proxy configuration. Custom accepts addresses such as `http://127.0.0.1:7890` or `socks5://127.0.0.1:7890`. **Test model** makes a real completion request and may incur charges; tests are not saved to history. Network/TLS errors identify the route and error category; certificate validation stays enabled.
3. Select source and target languages, such as Chinese → English or Spanish → Chinese.
4. Enable the global hotkey (default **F9**) and optionally record another combination. Hotkeys are suspended while settings are open. **Save and apply**, then close settings to enable the new binding; closing without saving restores the last applied binding. Overlay/macro conflicts are checked.
5. In game, press the hotkey to open the topmost translator. **Enter** translates and **Shift+Enter** inserts a newline. Clicking outside or switching back to the game hides it; moving the pointer away alone does not. Press the hotkey again to restore the window with its input and output intact. The hotkey also hides it manually; **Esc** hides it and cancels the request.
6. Successful translations are **automatically copied** so you can switch back to the game and paste manually. Requests continue while hidden and copy the result without reopening the window. Failed/cancelled requests and **Test model** leave the clipboard unchanged. The manual copy buttons remain available, including those on history entries. Messages are never automatically pasted or sent.

Prompts use the currently selected AOE2 / AOE4 context, including civilizations, units, resources, age-up calls and team tactics. Translation does not require a bound player or active match polling. Quality and latency depend on the provider. Borderless/windowed game mode is recommended; exclusive fullscreen may hide desktop windows.

### Privacy and local history

- Text is sent to your configured provider, without match data, player profiles or the full translation history.
- Remote endpoints require HTTPS; localhost/loopback HTTP endpoints and local models without authentication are supported.
- Settings remain in `%LOCALAPPDATA%\AoEOverlay\translation\settings.json`. API keys are protected with Windows DPAPI for the current user and cannot be directly migrated to another user/computer.
- History lives in **`translation_history\history.json` beside `AoEOverlay.exe`**. Use **Open history folder** to locate it. Legacy history is migrated automatically; the old file is deleted only after a successful write. Existing destination history is never overwritten by migration.
- History is plain text (original, result, language pair, time and game), never broadcast over WebSocket. Copy individual translations, delete individual entries, or clear all with confirmation. The program directory must be writable; protected directories such as Program Files can prevent saving/migration, so a writable portable folder is recommended.
- Total timeout defaults to **90 seconds**, configurable from **10–300 seconds**, with a 15-second connection timeout. Timeout and manual cancellation have separate messages. Duplicate submissions are blocked; hiding does not cancel requests. Esc or closing cancels locally, but provider charges may still apply.

### Verification

```powershell
dotnet run --project tests/TranslationChecks/TranslationChecks.csproj -c Release
dotnet build AoE4OverlayCS.csproj -c Release
dotnet run --project tests/OverlayChecks/OverlayChecks.csproj -c Release
node tests/OverlayChecks/html-checks.cjs
```

Checks use simulated HTTP and do not require a real API key or consume model quota.

## Screenshots

<img width="1149" height="236" alt="overlay-preview-1" src="https://github.com/user-attachments/assets/e24325f7-cda0-432e-bdf5-1dbd8ce83d10" />
<img width="972" height="245" alt="overlay-preview-2" src="https://github.com/user-attachments/assets/9be65e8d-ebb0-4341-8469-6cccc6c20b45" />
<img width="836" height="624" alt="main-window-preview" src="https://github.com/user-attachments/assets/4afb1df9-bad1-47e1-a2f9-4d3a05ae3faf" />

## Version

- Current version: `3.0.0`
- Program name: `AoE Overlay` (`AoEOverlay.exe`)
- Target frameworks: `net10.0-windows`
- Full map and civilization index: [`docs/全量图谱索引.md`](./docs/全量图谱索引.md)

## Project Purpose

This project is not a game mod or an injected plugin. It is a standalone Windows desktop tool.

It uses public API data plus a transparent topmost window and a local HTML/WebSocket output pipeline to render match overlays. The settings page switches between AOE4 (`aoe4world.com`) and AOE2 (`aoe-api.worldsedgelink.com`). The two player identities are stored separately.

Typical use cases:

- Personal match lookup for the latest game
- Stream / recording overlay display
- Custom HTML overlay styling and debugging
- Local development using the WebSocket payload as a reusable data source

## Technical Architecture

### High-Level Structure

```text
AoE Overlay (AoEOverlay.exe)
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
│  ├─ CivNameTranslator
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
- Frameworks: `.NET 10`
- JSON: `Newtonsoft.Json`
- WebSocket server: `Fleck`
- Global hotkeys: `NHotkey.Wpf`
- Fallback keyboard handling: `Win32 Low-Level Keyboard Hook`
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
- click-through mode when locked, gold thick border
- drag/resize when unlocked, red thick border
- `30%` background opacity when locked
- immediate map and civilization localization updates
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
- Support localized civilization name display (25 civilizations)

### Overlay Control

- Global hotkey to show / hide overlay
- Global hotkey to lock / unlock overlay position
- Customizable position hotkey (with recording)
- Automatic fallback to low-level keyboard hook if system hotkey registration fails
- Unlock mode supports drag and resize
- Lock mode supports click-through behavior
- Gold thick border when locked, red thick border when unlocked

### Macro Automation (2.0 New)

- Key-macro automation: press the trigger hotkey to start/stop looping a key sequence
- Combos (Ctrl+A) and digit keys (control groups), recordable or manual input with visual key chips; recording captures keyboard keys only, not mouse actions
- Both looping and Test block physical keyboard/mouse input (including movement/clicks/wheel) during each round for at least 500 ms, or until a longer sequence finishes. Input is allowed between rounds. Blocked input is discarded, not replayed; hook installation failure prevents sending and reports an error
- Stop or exit lets an already-started round finish and release simulated keys before restoring input. Restart and Test are refused during cleanup to prevent overlapping sequences
- Interval slider is 1-30 s. A larger value such as 100 can be typed; the slider then stays at its maximum. Typing applies immediately. Repeat 1-99 times or infinite; runtime changes apply immediately
- Input idle wait: sending is deferred while keyboard/mouse is in use, so gameplay is not disrupted
- Shift monitor (2.1.1): when checked, an infinite loop checks Shift only during the last second of the interval; if held, it waits and sends the combo immediately on release, without an extra idle wait
- Background SendInput scan-code injection without window focus switch; trigger hotkey conflict detection
- Live status display and a Test button

### Match History

- Display recent match history
- Show both team rosters
- Show current bound player's result and rating diff
- Chinese UI displays localized map names
- Chinese UI localizes mode names (rm prefix → 排位赛)
- Chinese UI localizes match results (win → 赢, loss → 输)
- Player names are clickable, linking to their aoe4world.com profile
- Match IDs are clickable, linking to the aoe4world.com match detail page

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
│  ├─ CivNameTranslator.cs
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
- .NET SDK `10.0.301` or a compatible SDK resolved by `global.json`

### Development Run

```bash
dotnet build "AoE4OverlayCS.csproj"
dotnet run --project "AoE4OverlayCS.csproj"
```

### Run Executable Directly

After building:

```text
AoEOverlay.exe
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
- overlay show/hide hotkey
- overlay position lock/unlock hotkey
- overlay geometry
- font size
- team gap
- civilization stats color
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
- bilingual civilization name translation (25 civilizations)
- immediate overlay map and civ name refresh after language change
- immediate match history map and civ name refresh after language change

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
