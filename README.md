# AoE4 Overlay (C# Version) User Guide
<img width="672" height="223" alt="44-1" src="https://github.com/user-attachments/assets/814d7e31-2069-4a9f-ab76-1310f43c4878" />
<img width="786" height="592" alt="7" src="https://github.com/user-attachments/assets/5b6b9728-d865-4c9a-b193-b275d56ed33d" />
<img width="720" height="264" alt="33" src="https://github.com/user-attachments/assets/102dc919-4319-4ecb-a063-2b49d7d8348e" />
<img width="782" height="590" alt="22" src="https://github.com/user-attachments/assets/540fe8a1-9819-4ad7-a39f-76f6e14cb1da" />
<img width="781" height="585" alt="11" src="https://github.com/user-attachments/assets/e352f814-61a1-40b7-9951-c2ed2dacb505" />

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
