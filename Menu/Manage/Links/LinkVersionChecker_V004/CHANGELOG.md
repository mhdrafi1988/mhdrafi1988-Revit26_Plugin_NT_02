# Changelog — Link Version Checker

## V004 — 2026-10-17

### Fixed
- **Cloud links were invisible.** V003 collected in-model links only from `ExternalFileUtils.GetAllExternalFileReferences`,
  which never returns ACC / Autodesk Docs links: a cloud `RevitLinkType` is **not** an `ExternalFileReference`
  (`Element.IsExternalFileReference()` is false for it) and `GetExternalFileReference()` throws, so the old
  `try { ... } catch { continue; }` silently dropped every one. The scan now adds a second pass over
  `RevitLinkType` / `CADLinkType` / `PointCloudType` and reads their `Element.GetExternalResourceReferences()`,
  so ACC, BIM 360 and third-party resource-server links appear in the grid.
- Link types that carry neither a file nor a resource reference are listed as **Unknown / unresolved** instead of vanishing.
- `SafeFileName` now handles cloud paths (`Autodesk Docs://Project/Folder/Model.rvt`), which use forward slashes.
- Measured load times are applied to cloud links too - Revit's `LinkedResourceOpening/Opened` events report them.

### New
- **Source column + filter - 13 values in 4 groups.**

  | Group | Values |
  |---|---|
  | ACC | ACC - this project, ACC - other project, BIM 360 (legacy), Desktop Connector |
  | On disk | Desktop Connector, OneDrive / SharePoint, Local PC, Removable drive, Mapped drive, Network (UNC) |
  | Server | Revit Server, Other resource server |
  | No file | In model (imported), Unknown / unresolved |

  Group entries (`ACC (all)`, `On disk (all)`, ...) are pickable too. Desktop Connector appears in two groups
  on purpose - it is ACC content living on disk.
- **"Available only" (ticked by default)** - the Source and Type lists shrink to the values present in the scan,
  each with its count (`Network (UNC) - 3`). Untick to see all 13 Source values and all 7 Types, zero counts included.
- Both filter combos are editable / type-to-search.
- `SourceDetector` - drive-type classification (fixed / removable / mapped), UNC detection, mapped-drive
  resolution through `WNetGetConnection`, and folder-prefix detection for Desktop Connector and OneDrive roots
  (editable in `settings.json`). No Revit API, so it runs in the parallel probe stage.
- New checks: cloud link from another ACC project (Warning), Desktop Connector path (Warning),
  cloud-sync folder (Warning), removable drive (Warning), mapped drive (Info), legacy BIM 360 (Info),
  Revit Server (Info), unresolved path (Critical).
- Two new summary cards: **From ACC** and **On Disk**.
- Details panel shows **Source**, **Detected by** (the exact API test) and **Cloud ids** (project / model / region).
- Excel + CSV reports gain **Source** and **Source Group** columns; the Summary sheet gains **From ACC** and **On disk**.
- `Preview/LinkVersionChecker_V004_Preview.html` - clickable mockup of the new UI.

### Changed
- Every file-based rule and action now tests `LinkInfo.HasLocalFile` instead of `!IsCloud`, so Revit Server,
  imported CAD and unresolved rows are treated correctly rather than being mistaken for missing files.
- **Unload now** works on cloud links in In-Model mode (Revit can unload them); Pre-Open / Folder still only
  rewrites file-based links in the host's link table.
- Repath and Open folder are enabled only when there is a real file on disk.
- Namespace / folder / window title bumped to `LinkVersionChecker_V004`.

## V003 — 2026-10-14

### Fixed
- **Build error CS1061** — `'CADLinkType' does not contain a definition for 'Unload'`. The Revit 2026 API has no unload for CAD links (`CADLinkType` only offers `Reload`, `ReloadFrom`, `LoadFrom`). In-Model "Unload now" now unloads RVT/IFC links and **skips CAD links** with a clear reason (use Manage Links › CAD Formats, or Pre-Open mode, which still works for CAD via `TransmissionData`).
- Unload confirmation in In-Model mode says up front how many CAD links will be skipped.

### Removed
- `LinkActions.RunAllowingTransaction` helper (only existed for the CAD unload call).

### Changed
- Namespace / folder / window title bumped to `LinkVersionChecker_V004`.

## V002 — 2026-10-13

### New
- **Folder Audit mode** — scans every host model in a folder (optional subfolders) into one grid and report; Host column and Host filter appear in this mode.
- **Batch actions** — tick column, Tick problems / Tick all shown / Clear ticks; Upgrade, Rebuild IFC cache and Skip-load run on all ticked rows. Each file is upgraded once even when several hosts link it; host edits are grouped into one backup + one write per host.
- **Measured load times** — `LinkLoadTimeLogger` records real host and link open times from Revit events; Measured column + Last full open card; included in the Excel report.
- **Progress bar + Cancel** for scans and batch actions, with the window staying responsive.
- **Rule-based checks** — `ILinkRule` + `RuleEngine`; each check is its own class; findings carry severity, message and estimated seconds; multiple findings per link.
- **New checks** — duplicate links, imported (not linked) CAD, loaded point clouds (with ReCap support-folder size), all worksets open in big workshared links, absolute paths, cloud models.
- **Excel export** — formatted .xlsx (Summary / Links / Findings) via ClosedXML; CSV fallback.
- **Remembered settings** — last host + recent hosts dropdown, last folder, options, filters, window size, export folder.
- **In-Model** "Unload now" action; CAD links can be unloaded too. *(Didn't compile — fixed in V003.)*
- `LinkCheckStartup.Register(app, panel)` — one call adds 3 ribbon buttons and starts the logger.

### Changed
- Scan is a pipeline: link tables → parallel file-system probes (8 threads) → cached version headers → nested links → rules. Shared links are read once across hosts.
- `LinkInfo` now holds raw facts; status, severity, estimate and recommendations come from the rules.
- Pill colour follows severity (Critical / Warning / Info / OK) instead of a fixed status list.
- Backups go through `BackupService` (once per file per session, timestamped).
- Recommendations panel lists every finding, not just one.

## V001 — 2026-10-13
- First version: Pre-Open and In-Model checks, single-row actions, CSV export.
