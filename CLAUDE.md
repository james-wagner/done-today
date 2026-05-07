# Done Today — project context

A small Windows-only WPF (.NET 10) desktop to-do app for tracking what was accomplished each day. Always on top, dark blue-grey theme. Distributed as a freeware Windows `.exe`.

## Tech stack

- .NET 10 SDK, WPF (`UseWPF=true`), single-project
- C# 13, nullable enabled, implicit usings
- No external NuGet packages — pure framework code
- Persistence: JSON in `%APPDATA%\DoneToday\` via `System.Text.Json`

## Project layout

All source lives in the repo root. Single project file: `DoneToday.csproj`.

### Windows / dialogs

| File | Purpose |
| --- | --- |
| `MainWindow.xaml` / `.cs` | Top-level list, global hotkey, update banner, dialogs entry point |
| `SubtasksDialog.xaml` / `.cs` | Drill-in for a parent task's children |
| `DetailsDialog.xaml` / `.cs` | Notes editor (right-click on row) |
| `CompleteAtDialog.xaml` / `.cs` | Date/time + duration prompt when marking done |
| `HotkeyDialog.xaml` / `.cs` | Settings: hotkey, font size, update source, version, license link |
| `LicenseDialog.xaml` / `.cs` | Read-only EULA viewer (loads `LICENSE` from embedded resource) |
| `CompletedDialog.xaml` / `.cs` | History view from `completed.log.json`, ADO copy |
| `ArchivedDialog.xaml` / `.cs` | Browse / un-archive items |

### Models / helpers

| File | Purpose |
| --- | --- |
| `Settings.cs` | App settings (hotkey, font size, window geometry, update source) |
| `CompletedLog.cs` | Append-only log; `CompletedEntry` model; ADO formatter; duration formatter |
| `UpdateChecker.cs` | Reads `latest.json` from URL or folder, downloads new exe, swaps + relaunches via PowerShell helper |
| `AppPaths.cs` | Resolves the per-user data dir (`%APPDATA%\DoneToday`) |
| `DragAdorners.cs` | `DragGhostAdorner` + `InsertionLineAdorner` used by drag-reorder |
| `DateRange.cs` | Range presets used by Completed / Archived dialogs |

### Other

- `App.xaml` / `App.xaml.cs` — application, global theme brushes, reusable button styles, global value converters (`FontRatio`, `BoolToVisHidden`)
- `AssemblyInfo.cs` — minimal scaffold
- `icon.ico` — multi-size embedded icon
- `LICENSE` — proprietary EULA (also shipped as an embedded resource)
- `README.md` — public-facing readme
- `publish.ps1` — build / version-bump / publish / distribute orchestration

## Data layer

All app data lives under `%APPDATA%\DoneToday\`:

- `todos.json` — the active list. Top-level items + their `Children` (subtasks). Each `TodoItem` has `Id`, `Text`, `Details`, `Done`, `CompletedAt`, `TimeToComplete`, `CreatedAt`, `Archived`, `ArchivedAt`, `Children`.
- `completed.log.json` — append-only ledger of completions. Each `CompletedEntry`: `ItemId`, `Text`, `Details`, `CompletedAt`, `TimeToComplete`, `ParentItemId`, `ParentText`. This file is the source of truth for the ADO copy / completed-tasks dialog. It is **not** purged when the active list is archived or cleared.
- `settings.json` — `HotkeyModifiers`, `HotkeyVirtualKey`, `FontSize`, `WindowLeft/Top/Width/Height`, `UpdateSource`.

`AppPaths.DataDir` is the single source of truth for this directory. All three stores read and write through it.

## TodoItem semantics

- Each `TodoItem` has a stable `Id` (`Guid`). Items loaded from old JSON without an Id get one assigned at load time.
- Items (top-level and nested) are wired in/out via `MainWindow.WireItem` / `UnwireItem` — this attaches `PropertyChanged` and child `CollectionChanged` handlers that drive autosave and completion logging.
- The `Done` setter is the central event:
  - `false → true`: stamps `CompletedAt` (unless already set by the Mark-complete dialog) and **appends** a `CompletedEntry`.
  - `true → false`: clears `CompletedAt` and `TimeToComplete`, then **removes the most-recent matching log entry**.
- `MainWindow.SetCompletionTime(item, when, duration)` is the override path used by the Mark-complete dialog. It either flips `Done` with pre-set values, or — if already done — calls `CompletedLog.UpdateLatestByItemId` to mutate the existing log entry in place (no new entry).

## Build & run

```powershell
.\publish.ps1                                # Build Release and launch
.\publish.ps1 -BumpVersion                   # Bump patch (X.Y.Z → X.Y.(Z+1)), build, launch
.\publish.ps1 -BumpVersion -BumpType minor   # Bump minor
.\publish.ps1 -SetVersion 1.2.3              # Set explicit version
.\publish.ps1 -NoLaunch                      # Build only
.\publish.ps1 -Publish                       # Single-file .exe in dist\ (still framework-dependent)
.\publish.ps1 -Distribute "<folder>" -BumpVersion -ReleaseNotes "..."
```

What `publish.ps1` does:
1. Stops any running `DoneToday` process.
2. Optionally rewrites `<Version>`, `<AssemblyVersion>`, `<FileVersion>` in `DoneToday.csproj`.
3. `dotnet build -c Release` (or `dotnet publish` with single-file when `-Publish`).
4. If `-Distribute`: copies `DoneToday-<ver>.exe` into the target folder and writes `latest.json` next to it.
5. Launches the result unless `-NoLaunch`.

## Distribution / auto-update

Distribution is via **GitHub Releases** or any HTTP host / shared folder.

The app reads a manifest at `Settings.UpdateSource` (URL or folder path):

```json
{
  "version": "0.2.0",
  "exe": "https://github.com/james-wagner/done-today/releases/download/v0.2.0/DoneToday-0.2.0.exe",
  "notes": "..."
}
```

- `UpdateChecker.CheckAsync` fetches the manifest, compares `Major.Minor.Build`, returns the info if newer.
- `MainWindow.CheckForUpdatesOnStartup` runs this on `Loaded`. The Settings dialog has a **Check now** button that does the same on demand.
- When the user clicks **Install** in the banner: `UpdateChecker.DownloadAsync` fetches the new exe to `%TEMP%`, then `InstallAndRestart` writes a tiny PowerShell helper to `%TEMP%\DoneToday-Updater.ps1`, kicks it off, and exits. The script waits ~1s for the process to die, copies the new exe over, and relaunches.

`publish.ps1 -Distribute` produces the `DoneToday-X.Y.Z.exe` + `latest.json` pair ready to upload to GitHub Releases or drop into a OneDrive / Dropbox / network share.

## Theme

Defined in `App.xaml` resources. Key brushes:

- `Brush.Bg` (`#3A4456`) — window background, the lighter blue-grey "ground"
- `Brush.BgAlt` / `Brush.Surface` (both `#143A75`) — deep navy used by tiles, buttons, input boxes
- `Brush.SurfaceHover` (`#1F4D9A`) — hover accent on tiles
- `Brush.Accent` (`#5AA0F2`) / `Brush.AccentBright` (`#7CB8FF`) — primary blue accents
- `Brush.PriorityBg` / `Brush.PriorityBgHover` — applied to the top list item via `AlternationIndex=0` triggers
- `Brush.Text` / `Brush.TextMuted` / `Brush.TextDim` — three tiers of text contrast

Reusable button styles: `AccentButton`, `GhostButton`, `RowIconButton`.

Font: **Trebuchet MS** with **Segoe UI** fallback. The Window's `FontSize` is initialized from `Settings.FontSize` (slider 11–48 in the Settings dialog). Tiny print scales via the `FontRatio` value converter (typical ratios: 0.7 for hint/status text, 0.81 for icon buttons).

## Conventions

- Namespace: `DoneToday`. AssemblyName / RootNamespace default from `DoneToday.csproj`.
- XAML class refs: `x:Class="DoneToday.<WindowName>"`.
- One window/dialog per `*.xaml` + `*.xaml.cs` pair.
- Several small types (`FontSizeRatioConverter`, `BoolToVisibilityHiddenConverter`, `DoneToBrushConverter`, `DoneToTextDecorationsConverter`, and the `TodoItem` model itself) live at the bottom of `MainWindow.xaml.cs` for proximity to where they're used.
- Avoid adding NuGet packages without strong justification — the goal is a tiny single-file build.
- Don't write speculative comments; prefer self-explaining code with a short comment only where intent is non-obvious.

## License

Proprietary freeware EULA. Lives in `LICENSE` in the repo root and is also embedded as a resource (so the in-app **License** link in Settings can render it).

- Free for personal and internal business use.
- No redistribution, modification, reverse engineering, or resale.
- Future versions may be re-licensed (commercial, paid, etc.).
- This is **not** an OSI-approved open-source license. Do **not** swap it for MIT / Apache / GPL — once a version ships under a permissive license, that version is forever forkable.

## Things to be careful about

- **Backward-compatible JSON.** `%APPDATA%\DoneToday\` may already contain real user data. Schema changes must rely on missing fields → default values. Don't rename or remove a property without a migration story.
- **Toggling Done is the log's only entry/exit point.** Append on `false → true`, remove latest matching on `true → false`. The Mark-complete dialog's "edit existing" path uses `CompletedLog.UpdateLatestByItemId` and intentionally does *not* append a new entry.
- **Don't add a permissive license.** Going restrictive → permissive later is fine; the reverse cannot be undone for already-released versions.
- **Don't change the namespace / exe name** without a coordinated migration. `publish.ps1` looks up the running process by name (`DoneToday`), and historical `latest.json` files reference exes named `DoneToday-X.Y.Z.exe`.
- **SmartScreen.** The binary is unsigned. Users who download from the internet will see a "Windows protected your PC" warning the first time. Don't fight this without a code-signing cert.

## Repository

- GitHub: <https://github.com/james-wagner/done-today>
- Default branch: `main`
- Public repo with a custom freeware license (no OSI license file).
