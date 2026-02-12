# AoE4 Overlay (C# 1.5.1) User Guide
<img width="661" height="164" alt="SnowShot_2026-02-12_19-38-08" src="https://github.com/user-attachments/assets/67fce948-c13b-439e-a0f9-60d8021ca9af" />
<img width="774" height="584" alt="SnowShot_2026-02-12_19-40-00" src="https://github.com/user-attachments/assets/c2c4a2bd-4caa-4fba-9c5e-13162ca68000" />
<img width="786" height="592" alt="7" src="https://github.com/user-attachments/assets/5b6b9728-d865-4c9a-b193-b275d56ed33d" />
<img width="780" height="588" alt="SnowShot_2026-02-12_19-40-53" src="https://github.com/user-attachments/assets/c008d99b-fdc9-49ff-a8b1-ddc9e994fd41" />
<img width="711" height="302" alt="SnowShot_2026-02-12_19-41-34" src="https://github.com/user-attachments/assets/1715c68f-a68b-4ffe-9eba-f88f1368df8d" />

This guide is for the C# / WPF refactored version located in the `AoE4_Overlay_CS` directory.

English | [中文版](./USAGE_CN.md)
## 1. Startup and Exit

- **Startup (Dev Mode)**
  - Enter the repository root and run:
    - `dotnet run AoE4OverlayCS.csproj`
- **Startup (Directly run executable)**
  - `AoE4OverlayCS.exe`
- **Single Instance**
  - Only one instance can run at a time. Launching a second one will prompt a message and exit automatically.
- **Exit**
  - **Close button (top-right)**: Minimizes to system tray (does not exit).
  - **Tray Context Menu -> Exit**: Fully exits the program, closing the Overlay and related services.
  - **Menu -> File -> Exit**: Fully exits the program, closing the Overlay and related services.

## 2. System Tray

- **Tray Icon**: Double-click to show the main window.
- **Tray Menu**:
  - `Open`: Shows the main window.
  - `Exit`: Exits the program (also closes the Overlay).

## 3. Menu Functions

- `File -> Html files`
  - Opens the `html` folder in the executable's directory (for viewing/editing HTML resources).
- `File -> Config/logs`
  - Opens the configuration directory and attempts to select the most recent log file if it exists.
- `Links -> App on Github`
  - Opens the project URL: `https://github.com/gearlam/AoE4_Overlay_CS`

## 4. Settings Page: Player & Hotkey Binding

- **Player Search/Binding**
  - Enter a player name or ProfileId in the Settings page to search. Once successful, it will be saved and used for:
    - Overlay display (in-game player info)
    - Loading history on the Games page
- **Hotkey (Global)**
  - Record/set a hotkey in the Settings page (recommended: `F12` or any unused key).
  - Function: Show/Hide the Overlay.
  - If the system hotkey registration fails (e.g., already in use), the program will automatically enable a keyboard hook as a fallback.

## 5. Overlay Operations (Display & Locking)

- **Show/Hide**
  - Toggle via the global hotkey.
- **Lock/Unlock (for adjusting position & size)**
  - **Unlocked**: Draggable and resizable; background is 100% black (for easier alignment).
  - **Locked**: Click-through (does not interfere with game operations); background is 50% black.
- **Username Background (by Team)**
  - Each player's name in the Overlay has a background color. Different teams use different colors for easy identification.
  - Colors are derived from the `TeamColors` in the configuration.

## 6. Games Page: Match History

- The Games page displays recent match history (default max 100 entries, adjustable in config).
- Team1/Team2 display format:
  - `Name [profile_id] (civilization)`

## 7. Config and Log Locations

### Config File
- `%LOCALAPPDATA%\\AoE4_Overlay_CS\\config.json`

### Common Log Files (Run Directory)
These files appear in the program's executable directory:
- `hotkey.log`: Debug log for hotkey registration and triggers.
- `dispatcher_error.log`: WPF Dispatcher unhandled exceptions.
- `domain_error.log`: AppDomain unhandled exceptions.
- `tray_error.log`: Tray initialization errors.

## 8. Troubleshooting

- **Hotkey not responding**
  - Ensure a hotkey is set and saved in the Settings page.
  - Check `hotkey.log` in the run directory to see if the key press is being captured.
- **No data on Games page**
  - Ensure a ProfileId is successfully bound in the Settings page.
  - Ensure your network can access the `aoe4world.com` API.
