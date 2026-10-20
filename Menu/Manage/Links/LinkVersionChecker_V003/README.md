# Link Version Checker V003 — Revit26_Plugin

Finds the linked files that slow down opening a Revit 2026 model, measures how long each one really takes, and fixes them — one at a time or in batches.

## Three modes

| Mode | What it reads | Needs a model open? |
|---|---|---|
| **Pre-Open** | One host `.rvt`: its link table (`TransmissionData`) and every link's version header (`BasicFileInfo`). No model is opened. | No |
| **In-Model** | The open project: links, nested links, imported CAD, linked images, open worksets, instance counts. | Yes |
| **Folder Audit** | Every host `.rvt` in a folder (optionally subfolders) — one combined grid and report. Skips `.0001.rvt` backups, `.ifc.RVT` caches, `_backup` folders and `_LinkCheckerBackup`. | No |

## Checks (rule-based)

Each check is a small class in `Core/Rules/Rules.cs`. To add one, implement `ILinkRule` and add it to `RuleEngine.DefaultRules`.

| Rule | Severity | Adds to estimate |
|---|---|---|
| Temporary upgrade (older RVT) | Critical | yes |
| Newer version (can't load) | Critical | — |
| Not found | Critical | 15 s |
| IFC cache missing / stale / older Revit | Warning | yes |
| Duplicate link (same file linked twice) | Warning | yes |
| Imported (not linked) CAD | Warning | — |
| Point cloud loaded (Warning if > 1 GB) | Info/Warning | — |
| Unreadable header | Warning | — |
| All worksets open in a big workshared link | Info | — |
| Nested link | Info | in brackets, not in total (pre-open) |
| Absolute path | Info | — |
| Cloud model | Info | — |

Estimate constants are in `LoadCostModel` (top of `RuleEngine.cs`) if you want to tune them.

## Measured load times

`LinkLoadTimeLogger` listens to Revit's `DocumentOpening/Opened` and `LinkedResourceOpening/Opened` events and records the real seconds each host and each linked RVT/IFC takes to open. Results go to
`%AppData%\Revit26_Plugin\LinkVersionChecker\loadtimes.json` (last 10 per file) and show in the **Measured** column and the **Last full open** card.

- It only logs once registered at startup (see Install), so measurements appear after you've opened models with it running.
- A parent link's time includes its nested links. CAD and point clouds aren't reported by these events.

## Batch actions

Tick rows (or use **Tick problems / Tick all shown**). Actions apply to ticked rows, or to the selected row if nothing is ticked.

| Action | Pre-Open / Folder | In-Model |
|---|---|---|
| Upgrade & Save | Older, **non-workshared** RVTs: backup → open in background → save in 2026 → close. Each file once, even if many hosts use it. | Same; links loaded in the open model are unloaded during the upgrade and reloaded after. |
| Rebuild IFC cache | Old cache → `.bak`; new one is written on the next open | Old cache → `.bak`, link reloaded now |
| Don't load / Unload | Marks links to stay unloaded on next open (one backup + one write per host) — RVT, IFC and CAD | Unloads RVT/IFC links now. CAD links are skipped: the Revit API has no `CADLinkType.Unload()` — use Manage Links, or Pre-Open mode |
| Repath (single) | Rewrites the host's link table | `RevitLinkType.LoadFrom` (RVT) |

Safety: every file is copied to `_LinkCheckerBackup\` beside it before the first change in a session; host edits are refused if the host is open in Revit or is a **workshared central**; workshared links are never upgraded (flagged for the owner). Long actions show progress with **Cancel** (stops between files). A summary of done / failed / skipped is shown afterwards and the grid rescans.

## Excel report

**Export to Excel** writes a formatted `.xlsx` with three sheets — **Summary** (per host: counts, estimate, last measured open), **Links** (all columns, rows coloured by severity, filters), **Findings** (one row per issue). If ClosedXML can't load, it falls back to a `.csv`.

## Settings remembered

Last host, recent hosts (dropdown), last folder, nested/subfolder options, filters, window size and last export folder — in `%AppData%\Revit26_Plugin\LinkVersionChecker\settings.json`.

## Install

1. Copy the `LinkVersionChecker_V003` folder into the repo, e.g. `Revit26_Plugin\Menu\ProjectTools\LinkVersionChecker_V003\`. V001/V002 can stay; namespaces don't clash. Point your ribbon wiring at V003 (V002 doesn't compile — see CHANGELOG).
   **If V002 is already in the repo, retire it** — it doesn't compile, and SDK-style projects build every folder. Add next to the other exclusions in `Revit26_Plugin.csproj` (adjust the path):
   ```xml
   <Compile Remove="Menu\ProjectTools\LinkVersionChecker_V002\**" />
   <Page Remove="Menu\ProjectTools\LinkVersionChecker_V002\**" />
   ```
   (`Page Remove` is needed too, or V002's XAML still generates code-behind for a class that no longer exists.)
2. Add ClosedXML to the `.csproj` (and keep `<UseWPF>true</UseWPF>`):
   ```xml
   <PackageReference Include="ClosedXML" Version="0.104.2" />
   ```
3. In your `IExternalApplication.OnStartup`, one line registers the logger and adds the three buttons:
   ```csharp
   RibbonPanel panel = application.CreateRibbonPanel("YourTab", "Project Tools"); // or your existing panel
   Revit26_Plugin_Revised.LinkVersionChecker_V003.Commands.LinkCheckStartup.Register(application, panel);
   ```
   and in `OnShutdown`:
   ```csharp
   Revit26_Plugin_Revised.LinkVersionChecker_V003.Commands.LinkCheckStartup.Unregister(application);
   ```
   (Prefer your own buttons? Register the logger only with `LinkLoadTimeLogger.Register(application.ControlledApplication)` and point buttons at `CmdLinkCheckPreOpen`, `CmdLinkCheckInModel`, `CmdLinkCheckFolder`; use `LinkCheckAlwaysAvailable` as the availability class for Pre-Open and Folder.)
4. If your project namespace differs, find/replace `Revit26_Plugin_Revised.LinkVersionChecker_V003` in the `.cs` files and the `x:Class`/`xmlns:infra` lines in the `.xaml`.

## Structure

```
Commands/      LinkCheckCommands.cs       3 commands, availability class, LinkCheckStartup (ribbon + logger)
Core/Models/   LinkInfo.cs, Finding.cs    facts per link + findings
Core/Rules/    RuleEngine.cs, Rules.cs    ILinkRule, engine, 12 rules, LoadCostModel
Services/      LinkScanner.cs             scan pipeline (link tables → parallel file probes → headers → nested → rules)
               FileProbe.cs               file-system facts, thread-safe (runs in parallel)
               RevitFileReader.cs         BasicFileInfo + TransmissionData, cached (main thread)
               ScanReporter.cs            progress + cancel while staying on Revit's thread
               LinkActions.cs             batch upgrade / IFC / skip-load, repath
               BackupService.cs           one backup per file per session
               LoadTimes.cs               load-time logger + store
               SettingsStore.cs           remembered settings
               ReportExporter.cs          .xlsx (ClosedXML) with CSV fallback
UI/            Infrastructure/            ObservableObject, RelayCommand, AsyncRelayCommand, BindingProxy
               ViewModels/                LinkCheckerViewModel
               Views/                     LinkCheckerWindow.xaml(.cs)
Preview/       LinkVersionChecker_V003_Preview.html   clickable UI mockup
```

## Threading note

Revit's API must only be called on Revit's main thread. The window is modal and every `await` resumes on that thread, so headers and link tables are read there; only plain file-system checks (`FileProbe`) run in parallel on background threads. This is what keeps the UI responsive and cancellable without breaking API rules.

## Status

Type-checked against stub Revit/WPF/ClosedXML types and every XAML binding verified against the view model, but **not built against the real Revit 2026 API** (not available here). Build in Visual Studio and test on a copy of a project first.
