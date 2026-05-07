# Done Today

A small Windows desktop app for tracking what you accomplished each day. Always on top, dark blue-grey theme, stays out of the way until you click it.

## Features

- **Click to mark done** — captures completion time and asks how long it took.
- **Right-click for notes** on any task or subtask.
- **Subtasks** with their own completion times, notes, and drag-to-reorder.
- **Drag-and-drop reorder** with visual ghost + insertion-line feedback.
- **Archive completed** — clears the active list while keeping history.
- **Completed-tasks log** with **Copy for ADO** that produces a clean date-grouped summary you can paste into Azure DevOps or a commit message.
- **Configurable global hotkey** to summon the window from anywhere.
- **Auto-update on launch** — checks a configurable URL or folder for `latest.json` and offers an inline install banner.
- **Multi-monitor safe** — remembers position/size, falls back to the primary monitor if the saved monitor isn't connected.

## Install

1. Download `DoneToday-X.Y.Z.exe` from the latest [Release](../../releases/latest).
2. Run it. The first launch creates `%APPDATA%\DoneToday\` for tasks, settings, and the completion log.
3. Optional: pin to the taskbar.

If Windows shows a SmartScreen warning, click **More info → Run anyway**. The binary isn't code-signed.

## Usage

| Action | How |
| --- | --- |
| Add a task | Type in the box, press **Enter** |
| Mark done (prompts for date/time + duration) | Click the row |
| Un-mark done | Click again |
| Edit completion time after the fact | **Ctrl+click** on a done row |
| Add or edit notes | Right-click the row |
| Edit task title | Click the **✎** button |
| Open subtasks | Click the count badge (e.g. `2/5  ▸`) |
| Reorder | Drag a row, or **Alt+↑/↓** |
| Delete | Click **✕**, or press **Delete** on a selected row |
| Archive completed items | Bottom row **Archive completed** button |
| Browse / unarchive | **🗃** button |
| Completed log / ADO copy | **📋** button |
| Settings (hotkey, font size, update source, license) | **⚙** button |

## Data

Everything is stored under `%APPDATA%\DoneToday\`:

- `todos.json` — active list (and archived items)
- `completed.log.json` — append-only log of every completion (timestamp, duration, parent context)
- `settings.json` — hotkey, font size, window geometry, update source

The files are JSON, so another tool (or an AI agent like Claude Code) can read them directly.

## Auto-update

In **⚙ Settings → Updates**, set the source to a URL or folder containing a `latest.json`:

```json
{
  "version": "0.2.0",
  "exe": "https://github.com/james-wagner/done-today/releases/download/v0.2.0/DoneToday-0.2.0.exe",
  "notes": "What changed in this release."
}
```

On launch, the app fetches this manifest. If `version` is newer than the running build, a banner appears at the top of the window. **Install** downloads the new exe, replaces the running one, and relaunches.

## Build from source

Requires the **.NET 10 SDK** on Windows.

```powershell
.\publish.ps1                    # Build Release and launch
.\publish.ps1 -BumpVersion       # Bump patch (0.1.0 → 0.1.1), build, launch
.\publish.ps1 -Publish           # Single-file .exe in dist\
.\publish.ps1 -Distribute "C:\path\to\release-folder" -BumpVersion -ReleaseNotes "..."
```

The last form drops `DoneToday-X.Y.Z.exe` plus an updated `latest.json` into the folder you point at — handy with a OneDrive / Dropbox / network share, or as the staging step before uploading to GitHub Releases.

## License

Free for personal and internal business use. See [LICENSE](LICENSE) for full terms.

Redistribution, modification, reverse engineering, and resale are not permitted. Future versions may be released under different terms.
