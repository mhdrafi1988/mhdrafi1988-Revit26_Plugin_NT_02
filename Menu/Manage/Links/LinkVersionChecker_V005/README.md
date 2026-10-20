# Link Version Checker V005 — Revit26_Plugin

Finds **every** linked file of a Revit 2026 model — ACC / Autodesk Docs, Desktop Connector, network, local, Revit Server — shows where each one comes from, **whether the ACC copy you are using is the current one**, measures how long it really takes to open, and fixes the slow ones, one at a time or in batches.

## Three modes

| Mode | What it reads | Needs a model open? |
|---|---|---|
| **Pre-Open** | One host `.rvt`: its link table (`TransmissionData`) and every link's version header (`BasicFileInfo`). No model is opened. | No |
| **In-Model** | The open project: links, nested links, imported CAD, linked images, open worksets, instance counts. | Yes |
| **Folder Audit** | Every host `.rvt` in a folder (optionally subfolders) — one combined grid and report. Skips `.0001.rvt` backups, `.ifc.RVT` caches, `_backup` folders and `_LinkCheckerBackup`. | No |

## Source — where each link lives

Every row carries a **Source**, shown in its own sortable column and filterable on its own or by group.

| Group | Value | How it is detected |
|---|---|---|
| **ACC** | ACC — this project | `ExternalResourceReference`, `ProjectId` == host's |
| | ACC — other project | `ProjectId` differs (covers another hub / account) |
| | BIM 360 (legacy) | cloud path starts `BIM 360://` |
| | Desktop Connector | disk path under an ACCDocs / DC root (prefix, editable in `settings.json`) |
| **On disk** | OneDrive / SharePoint | disk path under `%OneDrive%` (prefix) |
| | Local PC | `DriveType == Fixed` |
| | Removable drive | `DriveType == Removable` |
| | Mapped drive | `DriveType == Network`; real UNC via `WNetGetConnection` |
| | Network (UNC) | path starts with a double backslash |
| **Server** | Revit Server | `ModelPath.ServerPath == true` (`RSN://`) |
| | Other resource server | an external resource server that is not Autodesk's |
| **No file** | In model (imported) | `ImportInstance.IsLinked == false` / embedded image |
| | Unknown / unresolved | path blank or `GetAbsolutePath()` failed |

**Why cloud links were missing before V004:** an ACC link is not an `ExternalFileReference`. Revit exposes it as an
`ExternalResourceReference`, so `ExternalFileUtils.GetAllExternalFileReferences` never returns it. V004 adds a second
collection pass over `RevitLinkType` / `CADLinkType` / `PointCloudType` that reads `GetExternalResourceReferences()`.

**"Available only"** (ticked by default) shrinks the Source and Type lists to the values actually present in the scan,
each with its count. Untick it to see all 13 Source values, zero counts included.

Desktop Connector and OneDrive **override** Local PC — they are the true source. Their folder roots live in
`settings.json` (`DesktopConnectorRoots`, `CloudSyncRoots`); add Dropbox or Google Drive roots there if you want
those flagged too.

Path type (Absolute / Relative / Server-relative) stays in its own **Path** column — it is not mixed into Source.

## Sync — is the ACC copy current? (V005)

Two independent signals, answering two different questions. Press **Check Cloud Status** (toolbar, beside Scan) for the first one.

| Dot | State | Means | Detected by | Modes |
|---|---|---|---|---|
| ● green | **Current** | this *is* the newest version in ACC | `ResourceVersionStatus.Current` | In-Model |
| ● green | **Downloaded** | the bits are on this PC | file attributes | all three |
| ○ green | **Not downloaded** | in ACC, still a placeholder here | `RECALL_ON_DATA_ACCESS` / `OFFLINE` / reparse point | all three |
| ● amber | **Out of date** | a newer version is published in ACC | `ResourceVersionStatus.OutOfDate` | In-Model |
| ● red | **Failed** | invalid reference, missing server, or the call threw | try/catch per link | In-Model |
| ◌ grey | **Unknown** | ACC could not answer — offline or signed out | `ResourceVersionStatus.Unknown` | In-Model |
| ◌ grey | **Not checked** | the button has not been pressed yet | — | all three |
| ◌ grey | **Not in this mode** | no Revit session to ask ACC with | — | Pre-Open, Folder |
| (blank) | **—** | not an ACC or cloud-sync link | — | all three |

The column sorts problems-first, not alphabetically.

**Why it is a button and not part of the scan.** `GetResourceVersionStatus()` may be a network round trip per link and
Autodesk documents neither its cost nor its offline behaviour. Keeping it on demand means a scan stays fast, works with
no network, and never stalls on a slow link. Progress and Cancel work; rows the run had not reached stay *Not checked*.

**Why Pre-Open and Folder Audit cannot answer.** They read the host `.rvt` from disk — no model is opened, so there is no
live `ExternalResourceReference` and no sign-in. Those rows say *Not in this mode* rather than showing a guessed green.
Desktop Connector and OneDrive rows **do** answer in every mode, because that answer is on the file system.

**"Downloaded" is not "up to date".** A synced Desktop Connector file exposes no version to compare; only a true cloud
link does. The state deliberately reads *Downloaded*, never *Current*, for those rows.

**If the placeholder attributes are missing** on a particular PC — Autodesk does not document Desktop Connector's
behaviour — the state degrades to *Not checked*. It never claims a file is local when it is not.

**`Copy cloud reference info`** (details panel) puts `InSessionPath`, `ServerId`, `Version`, the version status and every
`GetReferenceInformation()` key/value on the clipboard. Those ACC keys are undocumented, so run it once on a real project
if you want a true tip-version check (APS Data Management, 3-legged OAuth) in a later version.

## ACC links only

A tick box at the front of the filter row. It shows the `SourceGroup.Acc` values — ACC this project, ACC other project,
BIM 360 legacy **and** Desktop Connector — and it **overrides every other filter**: Source, Type, Problems only,
Upgrade needed only, Host and the search box. Those controls grey out while it is on, so the override is visible instead
of looking like a bug. Remembered in `settings.json`.

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
| Cloud model (ACC, this project) | Info | — |
| Cloud link from another ACC project | Warning | — |
| ACC version out of date | Warning | — |
| Not downloaded yet (Desktop Connector / OneDrive) | Warning | yes |
| Legacy BIM 360 link | Info | — |
| Desktop Connector path | Warning | — |
| OneDrive / SharePoint sync folder | Warning | — |
| Link on removable drive | Warning | — |
| Mapped drive | Info | — |
| Revit Server link | Info | — |
| Path unresolved | Critical | 15 s |

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

**Export to Excel** writes a formatted `.xlsx` with three sheets — **Summary** (per host: counts, ACC out of date, not downloaded, estimate, last measured open), **Links** (all columns including **Sync**, **Cloud Version** and **Sync Checked**, rows coloured by severity, filters), **Findings** (one row per issue). If ClosedXML can't load, it falls back to a `.csv`.

## Settings remembered

Last host, recent hosts (dropdown), last folder, nested/subfolder options, filters (including **Source**, **Available only** and **ACC links only**), when the cloud status was last checked, the Desktop Connector / cloud-sync folder roots, window size and last export folder — in `%AppData%\Revit26_Plugin\LinkVersionChecker\settings.json`.

## Install

1. The folder lives at `Revit26_Plugin\Menu\Manage\Links\LinkVersionChecker_V005\`. Point your ribbon wiring at V005.
   **Retire V004** (and V003 if it is still there) — SDK-style projects build every folder. Add next to the other exclusions in `Revit26_Plugin.csproj` (adjust the path):
   ```xml
   <Compile Remove="Menu\Manage\Links\LinkVersionChecker_V004\**" />
   <Page Remove="Menu\Manage\Links\LinkVersionChecker_V004\**" />
   ```
   (`Page Remove` is needed too, or the old XAML still generates code-behind for a class that no longer exists.)
2. Add ClosedXML to the `.csproj` (and keep `<UseWPF>true</UseWPF>`):
   ```xml
   <PackageReference Include="ClosedXML" Version="0.104.2" />
   ```
3. In your `IExternalApplication.OnStartup`, one line registers the logger and adds the three buttons:
   ```csharp
   RibbonPanel panel = application.CreateRibbonPanel("YourTab", "Project Tools"); // or your existing panel
   Revit26_Plugin_Revised.LinkVersionChecker_V005.Commands.LinkCheckStartup.Register(application, panel);
   ```
   and in `OnShutdown`:
   ```csharp
   Revit26_Plugin_Revised.LinkVersionChecker_V005.Commands.LinkCheckStartup.Unregister(application);
   ```
   (Prefer your own buttons? Register the logger only with `LinkLoadTimeLogger.Register(application.ControlledApplication)` and point buttons at `CmdLinkCheckPreOpen`, `CmdLinkCheckInModel`, `CmdLinkCheckFolder`; use `LinkCheckAlwaysAvailable` as the availability class for Pre-Open and Folder.)
4. If your project namespace differs, find/replace `Revit26_Plugin_Revised.LinkVersionChecker_V005` in the `.cs` files and the `x:Class`/`xmlns:infra` lines in the `.xaml`.

## Structure

```
Commands/      LinkCheckCommands.cs       3 commands, availability class, LinkCheckStartup (ribbon + logger)
Core/Models/   LinkInfo.cs, Finding.cs    facts per link + findings
               LinkSource.cs              the 13 Source values, their groups, colours and filter list
               AccSync.cs                 the 9 Sync states, their glyphs, colours and sort order  [V005]
Core/Rules/    RuleEngine.cs, Rules.cs    ILinkRule, engine, 20 rules, LoadCostModel
Services/      LinkScanner.cs             scan pipeline (file refs + cloud resource refs → probes → headers → nested → rules)
               CloudSyncProbe.cs          on-demand ACC version check + the clipboard diagnostics dump  [V005]
               SourceDetector.cs          drive type, UNC, mapped-drive resolution, Desktop Connector / OneDrive roots
               FileProbe.cs               file-system facts incl. placeholder state, thread-safe (runs in parallel)
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
Preview/       LinkVersionChecker_V005_Preview.html   clickable UI mockup (Sync column + ACC-only filter)
```

## Threading note

Revit's API must only be called on Revit's main thread. The window is modal and every `await` resumes on that thread, so headers, link tables and the cloud version check (`CloudSyncProbe`) are read there; only plain file-system checks (`FileProbe`, including the placeholder attributes) run in parallel on background threads. This is what keeps the UI responsive and cancellable without breaking API rules.

## Status

Type-checked against stub Revit/WPF/ClosedXML types and every XAML binding verified against the view model, but **not built against the real Revit 2026 API** (not available here). Build in Visual Studio and test on a copy of a project first.

`GetResourceVersionStatus()`, `.Version`, `IsValidReference()`, `GetResourceShortDisplayName()` and `HasValidDisplayPath()` are confirmed present on `ExternalResourceReference` in the Revit 2026 API reference, and `ResourceVersionStatus` is confirmed as `Current` / `OutOfDate` / `Unknown`. What is **not** documented is the cost of the status call, its behaviour when signed out, and whether Desktop Connector sets the Windows placeholder attributes on every machine — which is why the check is on demand and why anything inconclusive stays "Not checked".
