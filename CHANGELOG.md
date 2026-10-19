# Changelog

All notable changes to SloperPro. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [Semantic Versioning](https://semver.org/) (MAJOR.MINOR.PATCH).

- **Plugin version** lives in `Revit26_Plugin.csproj` (`<Version>`).
- **Tool versions** live in `Shared/Services/ToolCatalog.cs`. Each tool has its own semantic version;
  the ribbon tooltip, the tool's window title and its error dialogs all read it from there.

Each release lists every tool whose version changed, under the plugin version.

## [4.1.0] — 2026-10-19

### Added
- **Island Roof Splitter V002 (2.0.0) on the ribbon.** Moved out of `Menu/RoofTools/WIP` to
  `Menu/RoofTools/IslandRoofSplitter_V002`, so it is now built and shipped, under the
  `Revit26_Plugin_Revised.IslandRoofSplitter.V002` namespace. It sits at the top of the **Roof Edit ›
  Island Splitter** pulldown, with V001 (WIP) below it. New in V002: an OUTPUT choice (roof only /
  roof, floor if it fails / floor only), a floor type picker, and an Element column in the islands
  grid showing what each island became. Its window title and version badge read
  `ToolCatalog.IslandRoofSplitterV002`.

**User note:** the Island Splitter pulldown button now opens V002; V001 stays in the dropdown and is
unchanged. Test V002 on a copy of your model first.

## [4.0.0] — 2026-10-19

### Changed
- **Root namespace renamed `Revit26_Plugin` → `Revit26_Plugin_Revised`** across all C# and XAML
  (`namespace`/`using` declarations, `x:Class`, `clr-namespace:`, and the command class names the
  ribbon passes to `PushButtonData`). The add-in entry class is now `Revit26_Plugin_Revised.App`,
  and the installer scripts write that `FullClassName` into the `.addin` manifest.
- **Project `<RootNamespace>` set to `Revit26_Plugin_Revised`**, so new files get the new namespace
  and embedded-resource names move with it: icon paths passed to `ImageUtils.Load` now start with
  `Revit26_Plugin_Revised.Resources.…`, and `Properties.Resources` loads
  `Revit26_Plugin_Revised.Properties.Resources`.
- Unchanged on purpose: the assembly name (`Revit26_Plugin.dll`), `pack://…/Revit26_Plugin;component`
  URIs, and the `%AppData%\Revit26_Plugin` / `Documents\Revit26_Plugin` settings and log folders, so
  existing settings and logs still load. No tool versions changed.

### Fixed
- **Ribbon icons are embedded again** — the `.csproj` clean-up in an earlier release dropped every
  `<EmbeddedResource>` line for `Resources/Icons/*.png`, so `ImageUtils.Load` could not find the
  ribbon icons. All PNGs under `Resources/Icons/` are now embedded through one wildcard; a new icon
  only needs to be dropped in its category folder.

**User note (breaking):** reinstall with the updated installer, or edit an existing `.addin` file so
`FullClassName` reads `Revit26_Plugin_Revised.App`; a manifest still pointing at
`Revit26_Plugin.App` will fail to load. Keyboard shortcuts and Quick Access Toolbar entries are keyed
to command class names and must be re-assigned.

## [3.10.2] — 2026-10-19

### Fixed
- **Ribbon tooltips read `ToolCatalog`** — every ribbon button whose tool is in `ToolCatalog` (64
  buttons) now shows the catalog title line, "Name — vX.Y.Z (build Vnnn)", instead of a build tag
  typed into the ribbon code ("Tool — V032"), which had drifted from the real versions. The
  description lines under the title are unchanged, and each dropdown's version-list tooltip follows
  automatically. Older versions without a catalog entry keep their existing tooltip. Done once in
  `RibbonLayoutHelper` (new `ToolCatalog.Find(string)` lookup by command class); no button names,
  labels or icons changed.

## [3.10.1] — 2026-10-19

### Fixed
- **Window titles and version badges come from `ToolCatalog`** — 56 tools showed a build tag
  typed into the window (caption "Tool — V012", header badge "V012"), which drifted from the real
  version. The caption is now `ToolCatalog`'s "Name — vX.Y.Z" and the header badge "vX.Y.Z", bound
  in XAML through `x:Static` (no view-model changes). Sub-dialogs (Import Preview, Show in 3D View,
  Review exported CAD) keep their own caption and show the tool's catalog version. Each tool gets a
  PATCH bump:
  Auto Place Sections v324.1.1 (V324), Annotation Overlap Detection v4.1.1 (V004), Auto Slope By Point v30.0.1 (V030), Auto Slope By Point (Multi Copies) v30.1.1 (MultiCopies30), Auto Slope By Point (Ridge) v3.1.1 (V003), Automated Section Placer v3.1.1 (V003), Bubble Auto Renumber v8.0.1 (V008), Bubble Auto Renumber v9.1.1 (V009), Bulk Rename v1.1.1 (V001), Callout To Section View Placement v21.1.1 (V021), Combined Roof Tools v3.1.1 (V003), Creaser Adv v12.0.1 (V012), Create Sections From Detail Lines v13.1.1 (V013), Detail Line Closed Loop v3.0.1 (V003), Auto Dim Detail Line v10.1.1 (V010), DWG Manager v3.1.1 (V003), DWG Statistics v5.1.1 (V005), DWG To DL(P) v14.0.1 (V014), DWG To Lines v7.0.1 (V007), Export DWG to Folder v4.1.1 (V004), Floors And Roof From Linked Rooms v13.1.1 (V013), Floors And Roof From Linked Rooms (Via Plan View) v6.1.1 (V006), Divide Inner Loops v12.1.1 (V012), Inner Loops And Perpendicular v7.1.1 (V007), Island Roof Splitter v0.2.1 (V001 (WIP)), Link Version Checker v3.0.1 (V003), Detail Lines From Links v10.1.1 (VA010), Auto Slope By Drain (Multi-Roof) v12.0.1 (V012), Auto Slope By Drain (Multi-Roof) v13.1.1 (V013), Multiple Points (1/2–1/4) v3.0.1 (V003), Outer Curve Divider v6.0.1 (V006), ParaManager v5.1.1 (V005), Plan From Scope Box v6.1.1 (V006), Reference Section Head Placer v15.1.1 (V015), Roof Detail Line Intersect v14.1.1 (V014), Roof Edge Around Sections v7.1.1 (V007), Roof Edge Element Sections v4.1.1 (V004), Roof Edge Vertex Reducer v9.0.1 (V009), Roof Name Builder v2.1.1 (V002), Roof Point Comparison v3.0.1 (V003), Roof Point Elevation Sync v5.0.1 (V005), Roof Tag v18.1.1 (V018), Roof Type Manager v3.1.1 (V003), Roof View Focus v4.1.1 (V004), Schedule Export / Import v7.1.1 (V007), Section Auto Renamer v26.1.1 (V026), Section View Auto Tagger v6.1.1 (V006), Sheet Auto Rearrange v28.1.1 (V028), Sheet View Arrange v1.2.1 (V001), Smart View To Sheet Placer v224.1.1 (V224), View Auto Renamer v6.1.1 (V006), Workset Creator v16.1.1 (V016), Workset Manager v14.1.1 (V014), Workset Renamer v5.1.1 (V005), Workset Renamer (From Excel) v5.1.1 (FX05), Worksets & Elements Browser v4.2.1 (WSEB004).

> **User note:** window captions now use the catalog tool name, so a few read slightly differently
> (for example "Curve Point Divider — V006" becomes "Outer Curve Divider — v6.0.1"). Nothing else changes.

## [3.10.0] — 2026-10-19

### Fixed
- **Bulk Rename v1.1.0** — the Manage ▸ **Bulk Rename** button now works. Its code sat in
  `Menu/RoofTools/New items`, which the build excluded, so the button pointed at a class that was
  never compiled. Moved to `Menu/Manage/BulkRename_V001` (namespace unchanged) and moved onto the
  UI standard colour tokens.

### Removed
- **Roof From Detail Lines V008 / V009** — the Roof Tools ▸ **Roof From Lines** dropdown is removed.
  Its source code is not in the repository, its git history or any backup, so both buttons could
  only show an error. It comes back when the source is found. Its `ToolCatalog` entry is kept so old
  log lines still name the tool.
- 48 committed `.zip` backups (24 MB), `Revit26_Plugin.csproj.bak`, stale copies of Export DWG V003
  and Roof Name Builder V002 in `New items`, and the excluded `LinkVersionChecker_V003_UPDATED` copy.
  Git is the version history.

### Changed
- **Folder structure** (no namespace, class, ribbon-button or GUID changes; the build compiles the
  same 2,575 files, byte for byte):
  - Wrapper levels removed, e.g. `TaperedEpsPattern2D_V006/Menu/RoofTools/Modelling/TaperedEpsPattern2D_V006/`
    → `TaperedEpsPattern2D_V006/`, `AutoSlopeByPoint_V032/AutoSlopeByPoint_V032/` → `AutoSlopeByPoint_V032/`
    (43 tools).
  - Folder names fixed: `DeatailLInes` → `DetailLines`, `Setup/Familiy` → `Setup/Family`,
    `Line&Points` → `LinesAndPoints`, `Project DWG` → `DWG`, `Work set Manager` → `WorksetManager`,
    `ByPoints_0` / `ByDrains_0` / `Create_01` / `Rename_01` / `Place_01` / `Links_02` lose their
    suffixes; `(1)`, `_1`, `_updated`, `_WOrking`, `_By_POints` / `_by_openings` normalised.
  - Auto Slope By Drain (Multi-Roof) V013 moved from `ByPoints` to `ByDrains`; Link Version Checker
    V003 moved into `Manage/Links` beside V004 / V005.
  - `.csproj`: 672 entries pointing at folders that no longer exist are removed. Six of them would
    have excluded live tools once the folders were renamed. Only `Menu/RoofTools/WIP` is excluded now.
  - CLAUDE.md: folder-layout rule added.

> **User note:** after pulling, delete the `bin` and `obj` folders (or run *Clean Solution*) before
> building, because the old build output still points at the old folder paths. No tool behaves
> differently.

## [3.9.0] — 2026-10-19

### Changed
- **UI standard v4.0 (`Shared/SharedStyles.xaml`)** — one look for every tool window: navy header,
  Revit-neutral body. Affects every window that uses the shared styles.
  - Window background is a soft grey (#F3F5F7) instead of light blue.
  - Text boxes and combo boxes are white with a visible border (the light-orange fill is gone).
  - Status colours unified: one green / amber / red / blue set replaces the per-tool tile, pill,
    banner and group colours.
  - Revit dialog density: buttons 30 px (were 38), inputs 26 px (were 32), 4 px corners, metric
    numbers 18 px semibold.
  - The main **Run** button (`RunButton`) is navy instead of red. Red is now only for destructive
    actions (`DangerButton`).
  - New semantic tokens for new code: `Surface*`, `Border*`, `ActionPrimary`, `Status{Ok,Warn,Error,Info}`
    with `Soft` / `Border` variants. All v3.x keys still exist; the per-tool status keys
    (`Tile*`, `Pill*`, `Soft*`, `Group*`, `Banner*`) are deprecated aliases and will be removed in a
    later release.
- **Auto Slope By Point v32.1.0 (V032)** — pilot for the UI standard: window uses the semantic
  tokens only, and the window title shows the version from `ToolCatalog`. Given its own
  `ToolCatalog` entry (it was falling back to a hard-coded 32.0.0). No behaviour change.

- **UI standard rolled out to 49 more tools** — the newest version of each tool now uses
  the semantic tokens instead of the deprecated per-tool colour keys (MINOR bump each, no behaviour
  change). Link Version Checker V004 also stops hard-coding its colours and now loads the shared
  styles. Tools already on the standard (no deprecated keys) are unchanged: Detail Line Closed
  Loop, Outer Curve Divider, Roof Edge Vertex Reducer, Multiple Points, Creaser Adv, Roof Point
  Elevation Sync, Roof Point Comparison, Tapered EPS 2D Pattern (keeps its own group colours),
  DWG To Lines, DWG To DL(P). New `ToolCatalog` entries for the versions the ribbon actually opens:
  Auto Slope By Drain (Multi-Roof) V013 and Bubble Auto Renumber V009.
  - Detail Lines From Links v10.1.0 (VA010)
  - Auto Dim Detail Line v10.1.0 (V010)
  - Floors And Roof From Linked Rooms v13.1.0 (V013)
  - Floors And Roof From Linked Rooms (Via Plan View) v6.1.0 (V006)
  - ParaManager v5.1.0 (V005)
  - Schedule Export / Import v7.1.0 (V007)
  - Annotation Overlap Detection v4.1.0 (V004)
  - Worksets & Elements Browser v4.1.0 (WSEB004)
  - Workset Manager v14.1.0 (V014)
  - Workset Renamer v5.1.0 (V005)
  - Workset Renamer (From Excel) v5.1.0 (FX05)
  - Auto Slope By Point (Ridge) v3.1.0 (V003)
  - Auto Slope By Point (Multi Copies) v30.1.0 (MultiCopies30)
  - Auto Slope By Drain v7.1.0 (V007)
  - Auto Slope By Drain (Multi-Roof) v13.1.0 (V013)
  - Divide Inner Loops v12.1.0 (V012)
  - Roof Detail Line Intersect v14.1.0 (V014)
  - Inner Loops And Perpendicular v7.1.0 (V007)
  - Ridge By Openings v70.1.0 (V070)
  - Ridge By Points v59.1.0 (V059)
  - Roof Tag v18.1.0 (V018)
  - Combined Roof Tools v3.1.0 (V003)
  - Roof Type Manager v3.1.0 (V003)
  - Batch Link DWG Family v2.1.0 (V002)
  - DWG Manager v3.1.0 (V003)
  - DWG Statistics v5.1.0 (V005)
  - Export DWG to Folder v4.1.0 (V004)
  - Plan From Scope Box v6.1.0 (V006)
  - Smart View To Sheet Placer v224.1.0 (V224)
  - Automated Section Placer v3.1.0 (V003)
  - Sheet Auto Rearrange v28.1.0 (V028)
  - Sheet View Arrange v1.2.0 (V001)
  - Roof Edge Around Sections v7.1.0 (V007)
  - Roof Edge Element Sections v4.1.0 (V004)
  - Roof Name Builder v2.1.0 (V002)
  - Island Roof Splitter v0.2.0 (V001 (WIP))
  - Link Version Checker v4.1.0 (V004)
  - Roof View Focus v4.1.0 (V004)
  - Create Sections From Detail Lines v13.1.0 (V013)
  - Auto Place Sections v324.1.0 (V324)
  - Callout To Section View Placement v21.1.0 (V021)
  - Reference Section Head Placer v15.1.0 (V015)
  - Section View Auto Tagger v6.1.0 (V006)
  - View Auto Renamer v6.1.0 (V006)
  - Bubble Auto Renumber v9.1.0 (V009)
  - Section Auto Renamer v26.1.0 (V026)
  - Delete Workset v1.1.0 (V001)
  - Delete Line Styles v1.1.0 (V001)
  - Workset Creator v16.1.0 (V016)

- **Every current tool window now uses `ToolWindowShell`** — the last seven windows that built
  their own layout (six tools) now use the shared header / body / footer shell: one scrolling body,
  status strip with progress in the footer, primary action → Close on the right. Master-detail
  windows (Link Version Checker, Tapered EPS 2D) size their body to the window so the grid / live
  preview stays in view. No behaviour change.
  - Worksets & Elements Browser v4.2.0 (WSEB004): 3D view picker dialog moved onto ToolWindowShell.
  - Delete Line Styles v1.2.0 (V001): window moved onto ToolWindowShell; light filter toolbar replaces the dark inline-coloured one; title/version from ToolCatalog.
  - Delete Workset v1.2.0 (V001): window moved onto ToolWindowShell; light filter toolbar replaces the dark inline-coloured one; title/version from ToolCatalog.
  - Batch Link DWG Family v2.2.0 (V002): window moved onto ToolWindowShell; live log moved into the body; title/version from ToolCatalog.
  - Tapered EPS 2D Pattern v6.1.0 (V006): window moved onto ToolWindowShell and now loads SharedStyles (its style keys previously fell back to default WPF chrome); preview stays in view.
  - Link Version Checker v4.2.0 (V004): window moved onto ToolWindowShell (busy overlay kept on top); title/version from ToolCatalog.
  - Auto Slope By Drain v7.2.0 (V007): window moved onto ToolWindowShell; footer Cancel · Run → Close (new Close button); title/version from ToolCatalog.

> **User note:** tool windows look different (grey background, white inputs, smaller buttons, navy
> Run button). Nothing about what the tools do has changed. Report any window where text is clipped
> or a control is hard to see.

## [3.8.0] — 2026-10-17

### Added
- **Export DWG to Folder v4.0.0 (V004)** — added to Setup ▸ Project Tools ▸ **Export DWG** dropdown
  as the top (newest) entry, labelled `Export DWG V004 #`; V003 and V001 stay available below it.
  Adds layer-property export modes, a post-export review step and an export manifest.
  Internal button name: `Btn_ExportDwgToFolder_V004`.
- **Workset Creator v16.0.0 (V016)** — added to the new Worksets panel ▸ **WS Creator** dropdown as
  the top entry, labelled `Workset Creator V016 #`; V015 stays below it. Internal button name:
  `Btn_WorksetCreator_V016`. Also given its first `ToolCatalog` entry, so ToolGuard's error dialog
  and daily log name the tool and version.
- **Worksets ribbon panel** — new panel on the SloperPro-Documents tab, after Manage, holding every
  workset tool: WS Browser, WS Creator, WS Renamer, WS Renamer XL, Delete Workset.
- **Link Version Checker v4.0.0 (V004)** — all three scan modes added to the top of the **Link
  Check** dropdown, labelled `… V004 #`; V003's three modes stay below. V004 sees ACC / Autodesk
  Docs cloud links that V003 silently dropped, and adds a Source column with a 13-value filter.
  Internal button names: `Btn_LinkVersionChecker_InModel_V004`, `_PreOpen_V004`, `_Folder_V004`;
  Pre-Open and Folder Audit keep their always-available class so they work with no project open.
  Own `ToolCatalog` entry added.
- **Links ribbon panel** — new panel after Worksets holding the **Link Check** dropdown.
- **DWG Tools panel** — the Setup **Project Tools** panel is renamed **DWG Tools**; its four buttons
  (DWG To DL(P), Export DWG, DWG Manager, DWG Statistics) were already all DWG tools. Nothing moved,
  and the separate **Family Tools** panel still marks the two family-editor-only DWG tools
  (Batch Link DWG, DWG To Lines).

### Changed
- **Manage panel** — workset tools moved out to the new Worksets panel and **Link Check** to the new
  Links panel. Manage now holds Overlaps, ParaManager, Del. Line Styles, Bulk Rename and Schedule I/O.
- **Plugin version** — `<Version>` in the .csproj corrected from 3.6.0 (it had been left behind at
  the 3.7.0 release) to 3.8.0.
- **Ribbon naming convention** — the most recently added version of a tool carries a trailing `#`
  in its dropdown label.

### Removed
- **Workset Manager (V013, V014)** — removed from the ribbon on request. The commands, windows and
  `ToolCatalog` entry stay in the project, so the buttons can be restored by re-adding the two
  `PushButtonData` entries and the `Pulldown_WorksetManager` wiring in `ManageRibbon.cs`.

### Removed
- **File Status Checker (V001, V002)** — the tool was deleted from the repo
  (`Menu\Manage\Links`, now excluded in the .csproj), so its ribbon buttons and `ToolCatalog`
  entry were removed with it.

### Fixed
- **Duplicate Link Version Checker V003 source** — a second copy of the V003 tool
  (`Menu\Manage\Links_02\LinkVersionChecker_V003_UPDATED\lvc`, 18 files with the same
  namespaces as the live tool) had landed in a compiled path and would have failed the build with
  duplicate type definitions. Excluded in the .csproj (Compile / EmbeddedResource / None / Page);
  the folder can be deleted outright.
- **Link load-time logger registered once.** V003 and V004 share
  `%AppData%\Revit26_Plugin\LinkVersionChecker\loadtimes.json`, so only V004's logger is
  registered now — registering both would double-handle Revit's link-load events. V003 still reads
  the same file.

#### User note
Workset Manager no longer appears on the ribbon. Every other workset tool is unchanged but now lives
in the **Worksets** panel instead of **Manage**, and Link Check has moved to the new **Links**
panel, where V004 is now the top set of entries.

---

## [3.7.0] — 2026-10-15

### Added
- **Export DWG to Folder v3.0.0 (V003)** — added to the Setup ▸ Project ▸ **Export DWG** dropdown,
  above V001 (V001 stays available). Includes scan scope, delete-after-export mode, a grouped
  tick/untick grid and log export (auto-save or save-as picker).
- **Roof Name Builder v2.0.0 (V002)** — Roof ribbon, new **Roof Edit** panel. Bulk renames roof
  types/instances with configurable INS/SLP/MAT codes.
- **Island Roof Splitter v0.1.0 (V001, WIP)** — Roof ribbon, **Roof Edit** panel. Work in progress:
  try it on a copy of your model.
- **Link Version Checker v3.0.0 (V003)** — new **Link Check** dropdown in the Manage panel
  (Pre-Open, In-Model, Folder Audit). Starts a load-time logger when Revit starts.

---

## [3.6.0] — 2026-10-14

### Added
- **AutoSlopeByDrain V007** (single-roof) registered in RoofToolsRibbon — the tool existed in source
  but had no ribbon entry; added to the `Pulldown_AutoSlopeByDrain` pulldown as third item
  (order: V012 → V011 → V007), with its own icon (`by_drain_16.png`) to distinguish it from the
  multi-roof entries. Internal button name: `Btn_AutoSlopeByDrain_V007`.

### Changed
- **ToolCatalog** — synced all 44 stale tool-version entries to match the highest version registered
  in the ribbon files. No tool public behaviour changed; tooltips, window titles and error dialogs
  now show the correct current version for every tool.

---

## [3.5.0] — 2026-10-13

### Added
- **InnerLoopDivider V012** added to RoofToolsRibbon Shape Points panel as primary pulldown button (order: V012 → V011 → V010).
- **DwgManager V003** added to SetupRibbon Project panel as primary pulldown button; V002 remains as secondary.
- **DwgStatistics V005** added to SetupRibbon Project panel as new pulldown button.

### Fixed
- **DwgManager V002** button had wrong namespace () — corrected to .

### Changed
- ToolCatalog:  version corrected to 12.0.0 / V012;  to 3.0.0 / V003;  to 5.0.0 / V005;  entry added at 5.0.0 / V005.

## [3.4.0] — 2026-10-13

### Added
- **TaperedEpsPattern2D** registered in ToolCatalog (version 6.0.0 / V006) — the tool existed in
  source (V001-V006) but had no catalog entry; ribbon tooltips, window titles and error dialogs now
  read from the catalog like every other tool.

### Fixed
- **TaperedEpsPattern2D ribbon (Insulation panel)**:
  - Btn_TaperedEpsPattern2D_V002 (new): correct button for V002 (V002.Commands).
  - Btn_TaperedEpsPattern2D_V004: corrected command class from V006.Commands to V004.Commands.
  - Btn_TaperedEpsPattern2D_V005 (previously wired to V002 code): corrected to V005.Commands.
  - Btn_TaperedEpsPattern2D_V006 (new): button for V006 (V006.Commands).
  - Pulldown primary is now V006 (highest); order V006, V005, V004, V002, V001 per CLAUDE.md
    convention (highest first, descending).

## [3.3.0] — 2026-10-08

### Added
- **DWG Manager v2.0.0 (V002)** — lists every CAD link and import, grouped Linked / Imported →
  view type, one row per file per view (a file placed in three views shows three rows).
  - Metric cards (CAD files, linked, imported, instances), filter, Select All / Clear Selection,
    sortable columns (sorting stays inside the groups), collapsible groups.
  - **Open Views** opens the views of up to 5 ticked rows.
  - **IDs** shows a row's element IDs; click an ID to select it and zoom to it. IDs not drawn in the
    active view are greyed out; this updates when you come back to the window after switching views.
  - **Delete** per row (inline confirmation with its own purge tick) and **Delete Selected (n)** in
    the toolbar and footer for any number of ticked rows (asks first).
  - **Purge on delete** removes a CAD link / import type only when no instance of it is left anywhere
    in the model; otherwise the type is kept and the log says why.
  - Log with Copy All / Copy Selected / Clear, and a completion summary
    ("X deleted | Y skipped | Z failed | N type(s) purged").
  - Replaces the unreleased V001 draft, which did not build on Revit 2026 and changed the model
    from outside a Revit API context. Actions stop if the active document is not the one the window
    was opened for; elements owned by another user are skipped.

  **User note:** Delete removes only the instances in the row's view. Purge never removes a CAD type
  that is still placed in another view. Ctrl+Z in Revit undoes a delete.

### Changed
- **Export DWG to Folder v2.0.0 (V001)** — you now choose what to scan and what to export:
  - **Nothing is scanned when the window opens.** A new **Scan Scope** card has Source (Linked /
    Imported) and View type (Plan / Section / Drafting / No Views) boxes and a **Scan** button. All six
    boxes start unticked every time; Scan stays off until at least one source and one view type are
    ticked. The scan collects only DWGs inside that scope (and skips the slow "find views for
    model-space imports" pass when only Drafting is ticked).
  - **Advanced list of the scanned DWGs**, grouped Source → View type with collapsible groups and
    "Collapse All / Expand All": Name, Source, View Type, Host View, Sheet, Output File, Element ID and
    Status columns; click a header to sort inside the groups; a filter box (every word must match);
    **Select All / Select None** and a header tick box that act on the shown rows; Space ticks the
    highlighted rows; virtualised for large lists. **Every row starts unticked.**
  - **Status pre-check** while the list loads: a linked DWG whose source file can't be found, or an
    imported DWG with no host view, shows ⚠ and counts under the new **Issues** tile. During an export
    each ticked row updates live: Queued → Exporting… → Exported / Skipped / Failed.
  - **Export (n)** (was "Run") exports only the ticked rows and shows how many are ticked. It is off
    until a folder is set and at least one row is ticked.
  - **Three progress bars** in the footer — Scanning, Loading list and Exporting — each with a count
    and a percentage (grey = not started, blue = running, green = done, amber = finished with failures
    or stopped). The window repaints while Revit works, so the bars move during a long scan or export.
  - **Reset** (footer, left) unticks the scope and clears the list and the bars.
  - Header tiles are now Scanned / Linked / Imported / Ready / Issues / Selected. The window is wider
    (920 px) for the grid, and the folder picker is a standard folder dialog.
  - Settings file format version 2: the scan scope is no longer remembered. Version-1 files load; their
    scope fields are ignored. Output folder, subfolders, sidecar and overwrite are still remembered.
  - Opening the tool now goes through `ToolGuard`, and a Revit request that Revit refuses no longer
    leaves the window stuck as busy.

**User note:** v1 scanned the whole model on open and exported every DWG that passed the source and
view-type boxes. In v2 those boxes only decide what is *scanned*; only the rows you tick are
exported. Ticked rows hidden by the filter are exported too (the log says how many).

## [3.2.0] — 2026-10-08

### Added
- **Sheet View Arrange v1.1.0 (V001)** — the "Views in Reading Order" grid is now an advanced grid:
  - **Tick / untick** — every view has a tick box. An unticked view is left out: it stays exactly
    where it is in Revit (grey-hatched in the preview), and the other views are laid out without it.
    Header tick, **Tick all / Untick all / Invert**, a tick box on every group header, and the Space
    bar for the selected rows. Everything starts ticked, so nothing changes until you untick a view.
    Views that are left alone anyway (owned by another user, or pinned while "Move pinned views" is
    off) show a disabled tick and are skipped by the bulk actions.
  - **Filter** — a search box (detail number, view name or view type; several words must all match),
    **Type / Status / Row** pop-ups with counts, a **Show: All / Ticked only / Unticked only** list,
    removable filter tags and "Clear filters". Bulk tick actions act on the *shown* rows only.
  - **Sort** — click a column header (ascending, descending, back to reading order); Shift+click adds
    a second sort. Empty cells ("–") always sort last.
  - **Group by** None / View type / Status / Layout row, with collapsible groups, "Collapse all /
    Expand all" and a "N view(s) · M ticked" summary on each header.
  - The Skipped counter now reads "pinned / owned / unticked", and the status line and warning list
    mention unticked views ("… stay where they are (Unticked) …").

### Changed
- **Sheet View Arrange** — the grid lists every view in reading (detail-number) order, so a view that
  is skipped or unticked keeps its place in the list instead of moving to the bottom; its "#" and
  "Row" show "–".
- **Sheet View Arrange** — the settings file gains a `GroupBy` field (format version 2). Version-1
  files load unchanged and default to no grouping.

**User note:** filtering, sorting and grouping only change what the list shows. A ticked view hidden
by a filter is still arranged (a blue banner under the grid says how many), and the layout order
always stays by detail number. Ticks are remembered across Refresh and Apply but not after the window
is closed; only the "Group by" choice is saved. With nothing unticked the tool behaves exactly as
v1.0.0 (verified against the previous planner on random sheets).

## [3.1.0] — 2026-10-08

### Added
- **Bulk Rename v1.0.0 (V001)** — new tool on the Manage panel that renames many project items at
  once. A "What to rename" list picks the kind of item:
  - **Line styles** (Object Styles > Lines), **line patterns** (the dash and dot patterns CAD users
    call line types), **arrowheads** and **fill patterns** (drafting and model).
  - **Rules** act on the ticked rows that match the filter: find/replace (optionally case sensitive or
    a regular expression with `$1` groups), add or remove a prefix or suffix, clean spaces, spaces to
    underscores, UPPER / lower / Title Case, and numbering with a pattern such as `{name}_{n}` (start,
    step and digits are adjustable). You can also type a new name straight into the grid.
  - **Live status per row**: Ready, Unchanged, Invalid (empty name or a character Revit does not
    allow), Duplicate (two items would get the same name, ignoring case) or Locked. Apply stays off
    until no ticked row has a problem.
  - **Apply** renames everything in one transaction, so one Ctrl+Z undoes the whole run. Swaps
    (A to B and B to A), chains and case-only changes (Dash to DASH) are done in a safe order through
    a temporary name.
  - Revit's built-in line styles, the solid fill pattern and items owned by another user are shown
    dimmed as Locked and never changed. An item that changed, or was taken by another user, since the
    list was loaded is skipped and logged; so is a name Revit refuses.

**User note:** a line style's category name is read-only in the Revit API, so a line style is renamed
through its graphics style. Arrowheads have no category in the API; they are found by their "Arrow Style"
parameter and through the dimension, text and tag types that use them. If a model shows no arrowheads or
Revit refuses a line style rename, the log says so. DWG To DL(P) V014 remembers its line style and hatch
choices by name, so renaming a style it remembers makes that saved choice fall back to its "missing style"
prompt.

## [3.0.0] — 2026-10-08

### Removed
- **Ribbon: every tool now lists only its latest two versions.** Buttons removed from the dropdowns:
  Elevation Sync V003, Edge Element V002, Roof View Focus V002, Scope Box V004, Sheet Placer V222,
  Rearrange V025 / V026, From Links VA006 / VA007 / VA008, Schedule I/O V003 / V004 / V005,
  DWG To DL(P) V011 / V012, By Drain V007.
- **Source folders of superseded versions deleted from the repository** (still in git history), including
  older versions that were already off the ribbon: Detail Line Closed Loop V001, Auto Dim Detail Line V008,
  Floors And Roof From Linked Rooms V011, Via Plan View V004, Annotation Overlap Detection V002,
  ParaManager V003, Workset Renamer V003 / FX03, Workset Manager V012, Worksets & Elements Browser WSEB002,
  Combined Roof Tools V001, Roof Point Comparison V001, Roof Point Elevation Sync V003,
  Ridge By Openings V068, Ridge By Points V057, Multiple Points V001, Roof Edge Vertex Reducer V007,
  Roof Detail Line Intersect V012, Roof From Detail Lines V007, Auto Slope By Point V028 /
  Multi Copies 28 / Ridge V001, Multi-Roof Slope By Drain V010, Roof Type Creator V001, DWG To Lines V005,
  DWG To Detail Lines V011 / V012, Automated Section Placer V001, Plan From Scope Box V004,
  Smart View To Sheet Placer V222, Sheet Auto Rearrange V026, Roof Tag V016, Create Sections V011,
  Roof Edge Around Sections V005, Roof Edge Element Sections V002, Roof View Focus V002,
  Section View Auto Tagger V004, Auto Place Sections V322, Callout To Section V019,
  Reference Section Head Placer V013, Bubble Auto Renumber V006, Section Auto Renamer V024,
  View Auto Renamer V004.
- Kept on disk but **not on the ribbon**: Auto Slope By Drain V007, Creaser Adv V010, Divide Inner Loops V009,
  Inner Loops And Perpendicular V005 and Outer Curve Divider V004. Combined Roof Tools V002 / V003 are built
  on these versions and stop compiling without them. They go when Combined Roof Tools moves to the
  current tool versions.

### Fixed
- **Detail Lines From Links VA010** — its button did nothing: the command class was never added when
  VA010 was introduced on 2026-10-04. The command now exists (same entry checks as VA009, run through
  `ToolGuard`), so VA010 and VA009 both open.

**User note:** Revit ties keyboard shortcuts and Quick Access Toolbar items to a button. Re-assign any you
had on the removed buttons above. The remaining buttons keep their internal names, command classes and
shortcuts. This is a MAJOR release because versions were removed (same as 2.0.0); if you still need one,
it can be restored from git history.

## [2.5.0] — 2026-10-08

### Added
- **DWG To DL(P) V014 (tool 14.0.0)** — new version of the DWG to detail lines tool, with a
  line style / hatch mapping choice:
  - **Single (global):** one global line style and one global hatch apply to every selected layer.
  - **Multiple (per layer):** each layer row has its own line style (lines) or hatch type (hatches);
    rows you don't touch follow the global values. "Apply global to all rows" resets them.
  - The mode, the global picks and per-layer picks are remembered per CAD layer name
    (`%AppData%\Revit26_Plugin\DwgToDlp\mapping.json`, versioned).

  - The "missing line style / hatch type" prompt now also offers **Create all remaining** and
    **Skip all remaining**, so a DWG with many new layers needs one click, not one per layer.
  - A real **progress bar** in the status strip while converting: percent, current layer and
    element count (lines first, then hatches), and a "Done: N placed..." summary at the end.

### Changed
- The **Detail Lines** pulldown in Project Tools is now **DWG To DL(P)**, and its items are labelled
  "DWG To DL(P) V0xx". V014 is listed first; V013, V012 and V011 stay below it. User note: button
  internal names are unchanged, so existing customisations keep working.
- V014 runs through `ToolGuard`, so unexpected errors are logged to `%AppData%\Revit26_Plugin\Logs`.

### Notes
- The global and per-layer dropdowns start on **(Match layer name)**, which is exactly the V013
  behaviour (use the style named after the layer, ask before creating a missing one), so V014
  behaves like V013 until you choose something else.

## [2.4.0] — 2026-10-08

### Changed
- **Ribbon: 17 panels → 13, every button shows its name.** Revit hides button names (and then
  whole panels) when the tab is wider than the window, starting from the right, which left the
  Manage panel icon-only. The tab is now narrower:
  - **Floor Tools** → merged into **Roof Tools** (as "Floor From Rooms").
  - **Sheet Place** → merged into **Sheet Create** (Rearrange, Arrange Views).
  - **Detail Line Create**, **Detail Line Process** and **Dimensions** → one new **Detail Lines**
    panel (From Links, Closed Loop, Line Dims).
  - View Create and View Place no longer end in a large single button: 4 tools stack as 2 + 2
    (`RibbonLayoutHelper.AddStackedButtons`: 4 → 2+2, 5 → 3+2, 7 → 3+2+2).
  - Shorter labels: Combined Roof Tools → Combined Tools, Roof Type Manager → Roof Types,
    Roof From Detail Lines → Roof From Lines, Sections From Lines → From Lines, Section Around
    Edges → Around Edges, Roof View Focus → Roof Focus, Section Head Placer → Head Placer,
    Section View Tagger → View Tagger, Auto Section Placer → Section Placer, Detail Line Closed
    Loop → Closed Loop, Overlap Detection → Overlaps, Worksets & Elements → WS Browser,
    Workset Manager → WS Manager, Workset Renamer → WS Renamer, Workset Renamer (Excel) →
    WS Renamer XL, Delete Line Styles → Del. Line Styles, Schedule Export/Import → Schedule I/O.
  - Internal button names, command classes, versions and tooltips are unchanged.

**User note:** Revit ties keyboard shortcuts and Quick Access Toolbar items to the panel a button
is in. These six buttons changed panel, so re-assign any shortcut or QAT entry you had on them:
Floor From Rooms, Rearrange, Arrange Views, From Links, Closed Loop, Line Dims (was Dimensions →
Detail Lines). All other buttons keep their shortcuts. On a narrow screen or with Windows display
scaling above 100 %, Revit may still hide some names.
## [2.3.4] — 2026-10-08

### Fixed
- **Delete Line Styles v1.0.2 (V001)** — every run failed with "Category is unexpectedly NULL"
  and rolled back, so no line style was ever deleted. The tool read a line style's name after
  deleting it; it now reads everything it needs first and looks each style up fresh. No other
  behaviour change.

## [2.3.3] — 2026-10-08

### Changed
- **Ribbon dropdowns** — every pulldown that lists coexisting versions of a tool (Manage, Roof
  Tools, View Tools, Sheet Tools, Setup, Dimensions, Detail Lines, Floor Tools panels) now lists
  the highest version number first, with every older version below it in descending order. Several
  pulldowns previously listed the "current" version first and a newer rebuild further down (e.g.
  Schedule Export/Import showed V005, V006, V007, V004, V003); they now read highest-to-lowest.
  The pulldown button's own icon is always the highest version's icon.

**User note:** only the dropdown order changed — no button's internal name, command class, or
behaviour changed, and the ribbon button face still shows only the tool name, never a version
number.

## [2.3.2] — 2026-10-08

### Changed
- **Delete Line Styles v1.0.1 (V001)** — has its own ribbon icon (line styles with a red delete
  badge) instead of the Detail Lines icon. No behaviour change.

## [2.3.1] — 2026-10-08

### Fixed
- **Delete Workset v1.0.3 (V001)** — the run failed with "The file-based central model could not
  be reached" whenever central was offline or its path was not reachable from this PC (unmapped
  drive, VPN down, detached or copied model). The tool now deletes the selected worksets you
  already own and skips the rest, with a log line for each, instead of aborting the whole run.

**User note:** when central is unreachable, only worksets already checked out to you (or all
worksets in a detached model) can be deleted. Reconnect and Synchronize with Central to delete
the others.

## [2.3.0] — 2026-10-07

### Added
- **Delete Line Styles v1.0.0 (V001)** — new tool on the Manage panel. Lists every custom line
  style with how many lines use it (model, detail, symbolic and sketch lines, including lines in
  groups and filled-region boundaries). Two modes:
  - **Unused line styles only** (default) — deletes styles no line uses.
  - **All custom line styles** — also deletes used styles; their lines are changed to a
    replacement style you choose (default `<Thin Lines>`) before the style is deleted.

  Revit's built-in line styles are never touched. Styles owned by another user, or whose lines
  are owned by other users, are shown as "Blocked" and skipped. A style is skipped (and left
  unchanged) if any of its lines cannot take the replacement style. Everything runs in one
  transaction, so Ctrl+Z undoes the whole run.

**User note:** edges overridden with the Linework tool cannot be read through the Revit API, so a
style used only there counts as unused; those edges revert to their default style if it is deleted.

## [2.2.2] — 2026-10-07

### Fixed
- **Delete Workset v1.0.2 (V001)** — no workset could ever be selected. The tool treated every
  workset you had not already checked out as "Built-in", and then blocked the one you did own as
  "the only editable workset". Now every user workset is deletable unless another user has it
  checked out ("In use"), some of its elements are owned by other users ("Blocked"), or it is the
  only user workset. The badge tooltip gives the reason.
- Selected worksets are checked out from central automatically when you press Delete Workset;
  any that cannot be checked out are skipped and logged. Revit's own `CanDeleteWorkset` check runs
  before each deletion.
- "Migrate elements to" now lists every workset not being deleted (it only offered "deletable"
  ones). A target is required when a selected workset has elements or is closed; an empty open
  workset can be deleted without one (it used to fail).
- Answering **No** to a per-workset confirmation now skips just that workset instead of rolling
  back the whole run.
- Toolbar buttons (Select All / Clear / Refresh) and "Closed" status pills were unreadable
  (white text on a light background).

**User note:** running the tool now checks the selected worksets out to you in central.
Synchronize with Central afterwards to publish the deletion and release them.

## [2.2.1] — 2026-10-07

### Fixed
- **Delete Workset v1.0.1 (V001)** — the tool crashed on open with "'Border' TargetType does not
  match type of element 'TextBlock'". The metric-strip labels used a tile (Border) style and the
  options separator used a Border style on a `Separator`; both now use the correct styles. No
  behaviour change.

## [2.2.0] — 2026-10-07

### Added
- **Sheet View Arrange v1.0.0 (V001)** — new tool in the Sheet Place panel ("Arrange Views").
  Re-orders the views already on the active sheet by their existing Detail Number (natural
  order: 1, 2, 10 … A1, A2, B1) and lays them out like a reading table: left → right, wrapping
  into rows top → bottom. Each row is spread across the full usable width, rows are spread to
  the full usable height, and views in a row share a bottom line so their titles align. The last
  row can be packed left (default), justified or centred. Views are only moved — never resized,
  rescaled or removed. Usable area = title block inset by margins; live to-scale preview; nothing
  moves if the views don't fit, and Apply is refused if the sheet changed after the preview.
  Pinned views are moved and re-pinned (option); views owned by another user or changed in
  central (Reload Latest needed) are skipped and reported. One Undo step. Settings saved in
  `%AppData%\Revit26_Plugin\SheetViewArrange\settings.json`.

## [2.1.0] — 2026-10-06

### Added
- **Delete Workset v1.0.0 (V001)** — new Manage tool. Select one or more user worksets from a
  data grid (non-deletable built-ins and the last remaining workset are shown greyed out), choose a
  migration target workset, and delete them in a single transaction group. Migratable elements are
  reassigned; view-specific and read-only-partition elements can optionally be hard-deleted. The
  real-time log panel shows per-step progress and a completion summary. Ribbon button added to the
  Manage panel.
- **Export DWG to Folder v1.0.0** — new tool in the Setup › Project Tools panel.
  Scans the active document for all embedded CAD links (`IsLinked = true`) and CAD
  imports, classifies each by source type (link / import) and view category
  (plan / section / drafting / no_views), and exports them to a user-selected folder.
  Linked DWGs are copied directly from their source path; imported DWGs are exported
  from their host view via `Document.Export`.  Optional sidecar `.txt` file per DWG
  records View Name, Associated Sheet, Element ID and Symbol Name.  Options: toggle
  linked / imported sources, per-view-type subfolder checkboxes, sidecar toggle,
  overwrite toggle.  Settings are persisted in
  `%AppData%\Revit26_Plugin\ExportDwgToFolder\settings.json`.

## [2.0.0] — 2026-10-02

### Removed
- Superseded tool versions and their ribbon buttons (only the newest of each tool is kept):
  Detail Lines From Links VA003/VA006, Schedule Export/Import V001–V003, Plan From Scope Box V003,
  Smart View To Sheet Placer V221, Sheet Auto Rearrange V024/V025, DWG To Detail Lines V002,
  Roof Edge Element Sections V001, Roof View Focus V001, Roof Point Elevation Sync V002,
  and the unused WSFL V011 workset tool.
- All `.zip` backup copies from the repository (still in git history).

### Changed
- Ribbon: a button with a single tool now runs it on click, and is no longer a one-item dropdown.
  Dropdowns remain only where they group different tools (By Point, By Drain, Ridge Lines, From Rooms).
- Ribbon labels show the tool name only; the version moved to the tooltip
  ("Name — vX.Y.Z (build Vnnn)"). Internal button names and command classes are unchanged.
- Tools moved from build tags to semantic versions. Starting version = build number
  (e.g. build V004 → 4.0.0); see the table below.

### Added
- Suite-wide error safety net (`ToolGuard`): every command and external-event handler is guarded,
  and unhandled errors from tool windows are caught, so a tool error no longer reaches or crashes Revit.
  Errors are shown in a dialog and written to `%AppData%\Revit26_Plugin\Logs\errors-YYYY-MM-DD.log`.
- Each tool window's title ends with its version, e.g. "Sheet Auto Rearrange — v26.0.0".

### User notes
- Old tool versions are gone from the ribbon. If you relied on one, tell the maintainer before upgrading.
- A failing tool now shows an error dialog naming the tool and the log file — send that log when
  reporting a problem.
- Revit keyboard shortcuts must be reassigned for removed versions and for the 11 tools that moved out
  of a one-item dropdown (Revit keys shortcuts by the button's ribbon path). Other buttons keep theirs.

### Tool versions at 2.0.0

| Tool | Version | Build tag |
|---|---|---|
| Annotation Overlap Detection | 2.0.0 | V002 |
| Auto Dim Detail Line | 8.0.0 | V008 |
| Auto Place Sections | 322.0.0 | V322 |
| Auto Slope By Drain | 7.0.0 | V007 |
| Auto Slope By Drain (Multi-Roof) | 10.0.0 | V010 |
| Auto Slope By Point | 28.0.0 | V028 |
| Auto Slope By Point (Multi Copies) | 28.0.0 | MultiCopies28 |
| Auto Slope By Point (Ridge) | 1.0.0 | V001 |
| Automated Section Placer | 1.0.0 | V001 |
| Batch Link DWG Family | 0.1.0 | Working build |
| Bubble Auto Renumber | 6.0.0 | V006 |
| Callout To Section View Placement | 19.0.0 | V019 |
| Combined Roof Tools | 1.0.0 | V001 |
| Creaser Adv | 10.0.0 | V010 |
| Create Sections From Detail Lines | 11.0.0 | V011 |
| Detail Line Closed Loop | 1.0.0 | V001 |
| Detail Lines From Links | 7.0.0 | VA007 |
| Divide Inner Loops | 9.0.0 | V009 |
| DWG To Detail Lines | 11.0.0 | V011 |
| DWG To Lines | 5.0.0 | V005 |
| Floors And Roof From Linked Rooms | 11.0.0 | V011 |
| Floors And Roof From Linked Rooms (Via Plan View) | 4.0.0 | V004 |
| Inner Loops And Perpendicular | 5.0.0 | V005 |
| Multiple Points (1/2–1/4) | 1.0.0 | V001 |
| Outer Curve Divider | 4.0.0 | V004 |
| ParaManager | 3.0.0 | V003 |
| Plan From Scope Box | 4.0.0 | V004 |
| Reference Section Head Placer | 13.0.0 | V013 |
| Ridge By Openings | 68.0.0 | V068 |
| Ridge By Points | 57.0.0 | V057 |
| Roof Detail Line Intersect | 12.0.0 | V012 |
| Roof Edge Around Sections | 5.0.0 | V005 |
| Roof Edge Element Sections | 2.0.0 | V002 |
| Roof Edge Vertex Reducer | 7.0.0 | V007 |
| Roof From Detail Lines | 7.0.0 | V007 |
| Roof Point Comparison | 1.0.0 | V001 |
| Roof Point Elevation Sync | 3.0.0 | V003 |
| Roof Tag | 16.0.0 | V016 |
| Roof Type Manager | 1.0.0 | V001 |
| Roof View Focus | 2.0.0 | V002 |
| Schedule Export / Import | 4.0.0 | V004 |
| Section Auto Renamer | 24.0.0 | V024 |
| Section View Auto Tagger | 4.0.0 | V004 |
| Sheet Auto Rearrange | 26.0.0 | V026 |
| Smart View To Sheet Placer | 222.0.0 | V222 |
| View Auto Renamer | 4.0.0 | V004 |
| Workset Manager | 12.0.0 | V012 |
| Workset Renamer | 3.0.0 | V003 |
| Workset Renamer (From Excel) | 3.0.0 | FX03 |
| Worksets & Elements Browser | 2.0.0 | WSEB002 |

## [1.0.0] — 2026

### Added
- Initial public release on the Autodesk App Store: 30+ tools across Roof, Floor, View, Sheet,
  Setup and Manage panels.
