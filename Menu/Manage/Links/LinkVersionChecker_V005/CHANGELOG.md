# Changelog — Link Version Checker

## V005 (5.0.0) — 2026-10-19 — on the ribbon

### Changed
- The **Sync** column is now headed **ACC Docs** and draws its state as a radio-button style dot
  (shared `AccStatusDot`): filled green for Current / Downloaded, hollow green for Not downloaded,
  amber for Out of date, red for Failed, grey ring while unanswered, faint ring for a non-ACC link.
  The text beside the dot, its tooltip, its sort order and the report columns are unchanged.
- Added to the plugin ribbon (Links › Link Check, top three entries) with button ids
  `Btn_LinkVersionChecker_*_V005`; commands run through `ToolGuard`; window title from `ToolCatalog`.

## V005 — 2026-10-18

### New
- **Sync column — is the ACC copy we are using the current one?** Nine states, each with a dot, a colour and a
  tooltip naming the exact test behind it:

  | Dot | State | Means | From |
  |---|---|---|---|
  | ● green | Current | this *is* the newest version in ACC | `ResourceVersionStatus.Current` |
  | ● green | Downloaded | the bits are on this PC (not a version check) | file attributes |
  | ○ green | Not downloaded | in ACC, still a placeholder here | `RECALL_ON_DATA_ACCESS` / `OFFLINE` |
  | ● amber | Out of date | a newer version is published in ACC | `ResourceVersionStatus.OutOfDate` |
  | ● red | Failed | invalid reference, missing server, or the call threw | try/catch |
  | ◌ grey | Unknown | ACC could not answer (offline / signed out) | `ResourceVersionStatus.Unknown` |
  | ◌ grey | Not checked | the button has not been pressed yet | — |
  | ◌ grey | Not in this mode | Pre-Open / Folder Audit has no session to ask with | — |
  | (blank) | — | not an ACC or cloud-sync link | — |

  The column sorts problems-first (`SyncSortOrder`), not alphabetically.
- **`Check Cloud Status` button** (toolbar, next to Scan) — queries `ExternalResourceReference.GetResourceVersionStatus()`
  and `.Version` for every cloud link, with progress and a working Cancel. **On demand, never during a scan:** the cost
  per link is undocumented and may be a network round trip, so scans stay fast and keep working offline. Cancelling
  leaves the rows it had not reached as "Not checked". Every call is wrapped per link — a failure degrades to
  Failed or Unknown and never shows a dialog or stops the run.
- **Desktop Connector / OneDrive download state, in all three modes.** `FileProbe.PlaceholderState()` reads the Windows
  placeholder attributes (`RECALL_ON_OPEN`, `RECALL_ON_DATA_ACCESS`, `OFFLINE`, reparse point) and falls back to
  allocated-vs-logical size via `GetCompressedFileSizeW`. No Revit API and no sign-in, so it runs inside the existing
  parallel probe. Anything inconclusive stays **Not checked** rather than claiming a file is local when it is not.
- **`ACC links only` tick box** — the `SourceGroup.Acc` values (ACC this project, ACC other project, BIM 360 legacy and
  Desktop Connector). It **overrides every other filter** — Source, Type, Problems only, Upgrade needed only, Host and
  the search box — and those controls grey out while it is on, so the override is visible rather than surprising.
- **Two new rules**, so the sync state feeds severity, the Check pill, the open-time estimate and both report sheets
  instead of living only in the new column:
  - `ACC_OUT_OF_DATE` (Warning) — names the version you have and says to reload the link.
  - `ACC_NOT_DOWNLOADED` (Warning) — adds `CloudDownloadBaseSeconds + MB × CloudDownloadSecondsPerMb` to the estimate,
    because that download is invisible in Revit and usually gets blamed on the model.
- **Two new summary cards** — *Out Of Date* (with a subtitle that says plainly when nothing has been asked yet) and
  *Not Downloaded*. The metrics row is now 10 tiles.
- **`Copy cloud reference info`** (details panel) — dumps `InSessionPath`, `ServerId`, `Version`, the version status,
  the short display name and every `GetReferenceInformation()` key/value to the clipboard. Those ACC key names are
  undocumented and vary, so this is how the real ones get learned from a real project instead of guessed.
- Details panel gains **Sync**, **Cloud version** and **Sync checked** rows.
- Excel: Links sheet gains **Sync**, **Cloud Version** and **Sync Checked**; Summary gains **ACC out of date** and
  **Not downloaded**. Same three columns in the CSV fallback.
- `settings.json` remembers `AccOnly` and `LastSyncCheckUtc`.

### Changed
- Namespace / folder / window title bumped to `LinkVersionChecker_V005`; ribbon button ids are now `LVC3_*`.
- `LinkScanner` calls `CloudSyncProbe.Initialise()` before the rules run, so cloud rows start as
  *Not checked* In-Model and *Not in this mode* in Pre-Open / Folder Audit.
- `LinkSourceInfo.IsSyncPath()` added for "a synced folder on disk" (Desktop Connector or OneDrive).

### Known limits
- **A pure ACC link cannot be checked in Pre-Open or Folder Audit.** Those modes read the host `.rvt` from disk with no
  Revit session and no sign-in, so there is no live `ExternalResourceReference` to ask. The rows say so.
- **"Downloaded" is not "up to date".** A Desktop Connector file exposes no version to compare — only a true cloud link
  does. The state deliberately reads *Downloaded*, never *Current*, for those rows.
- Desktop Connector's placeholder behaviour is not documented by Autodesk. If the attributes are absent on a given PC,
  the state degrades to *Not checked*.
- A real tip-version check against ACC would need the APS Data Management API with 3-legged OAuth — that is a later
  version; `Copy cloud reference info` collects the key names it would need.

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
