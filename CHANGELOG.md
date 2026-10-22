# Changelog

All notable changes to SloperPro. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [Semantic Versioning](https://semver.org/) (MAJOR.MINOR.PATCH).

- **Plugin version** lives in `Revit26_Plugin_Revised.csproj` (`<Version>`).
- **Tool versions** live in `Shared/Services/ToolCatalog.cs`. Each tool has its own semantic version;
  the ribbon tooltip, the tool's window title and its error dialogs all read it from there.

Each release lists every tool whose version changed, under the plugin version.

## [6.7.0] — 2026-10-22

### Added
- **Workset Memory v1.0.0** — new **WS Memory** button in the new **Memory** panel. Saves which worksets are open (and the
  active workset) as named sets per model, then reopens the model with exactly those worksets open.
  - The window lists the model's saved sets (Remember current, Overwrite, Delete) and a table showing, for each workset,
    whether it is open now, open in the set, and whether a reload opens or closes it.
  - **Reload worksets** closes the model and reopens it with the set's worksets open: Revit can't open or close
    worksets in a model that is already open. It is blocked while the model has unsaved changes. While the model
    closes, a blank placeholder project (`%AppData%\Revit26_Plugin_Revised\WorksetMemory\WorksetMemory_Placeholder.rvt`)
    is briefly active, because Revit can't close the active model; it is closed again afterwards.
  - Option (on by default): make the set's active workset current again after reopening.
  - The button also works with no model open: pick a remembered model and set, and it opens straight with those worksets.
  - Sets are kept per central model (every local copy shares them) in
    `%AppData%\Revit26_Plugin_Revised\WorksetMemory\memory.json`. Cloud (Autodesk Docs) models are supported.
- **Link Memory v1.0.0** — new **Link Memory** button in the **Memory** panel, next to WS Memory. Saves which Revit
  links are loaded as named sets per model, then loads and unloads links back to a set in one click, in the open model.
  - Same saved-set list and comparison table as Workset Memory, plus a run log.
  - Options: unload links the set had unloaded (on by default); also reload links already loaded, to pick up the
    latest file (off by default).
  - In a workshared local copy, links are unloaded for the current user only (Manage Links' "Unload for me"), and a
    link unloaded that way is restored without affecting other users. Loading or unloading for all users skips a link
    whose type another user has checked out, and names that user in the log.
  - Links in a closed workset, links whose file is missing and links no longer in the model are skipped and reported;
    the other links still load.
  - Sets are kept in `%AppData%\Revit26_Plugin_Revised\LinkMemory\memory.json`.

**User note:** a new **Memory** panel (after Worksets) holds the two new buttons as large buttons; no existing tool changed. Revit clears the Undo list when a link loads.

## [6.6.0] — 2026-10-22

### Added
- **Link Version Checker v6.0.0 (V006)** — new top entry in the **Link Check** dropdown (V004 and V003 stay below it,
  unchanged). One window for the open model that lists only the **.rvt links stored in an ACC Desktop Connector folder**
  (a path containing `\ACCDocs\`) and shows, for each:
  - **In model** — Loaded, Unloaded, Unloaded (for me), Closed workset or Not found.
  - **On this PC (Desktop Connector)** — Always kept (full green), Available locally (green outline), Online only (cloud),
    Sync pending, or Missing / other. Read from the Windows cloud-file state behind Explorer's icons; nothing is downloaded.
  - **Latest from ACC?** — Yes, Reload needed or Unknown. Compares the save stamp of the copy loaded in the model with the
    file on this PC. Online-only files are not compared, because reading them would download them.
  - Count tiles that filter the list, a name search, "Problems only", and Export to Excel.
  - Actions on the selected link: **Reload**, **Unload** (for you only in a workshared model), **Always keep on this
    device**, **Open folder**. Reload / Unload are refused, with the user's name, when another user owns the link.

**User note:** nested links, cloud-only (non-Desktop-Connector) ACC links, CAD and IFC links are not listed; the status bar
says how many other RVT links were hidden. V004 / V003 still cover those.

## [6.5.2] — 2026-10-22

### Changed
- **Section Auto Renamer v26.1.3** (and V025) — the Section Manager command and its rename handler now run through the
  plugin's standard error handling. A failure while opening the window or renaming now shows the full cause and is written
  to `%AppData%\Revit26_Plugin_Revised\Logs`, instead of a one-line "An unexpected error occurred" dialog. Running it with
  no project open now shows "Open a project before running Section Auto Renamer."

**User note:** no change to renaming; only error reporting differs.

## [6.5.1] — 2026-10-22

### Fixed
- **"An ItemsControl is inconsistent with its items source" in log lists.** Windows scrolled their log from inside the
  list-changed event, which made WPF throw when several lines arrived at once. The log now scrolls after the list has
  updated. Tools bumped: **Section Auto Renamer v26.1.2**, **Auto Slope By Point v32.3.1**,
  **Auto Slope By Drain (Multi-Roof) v13.1.3**, **Outer Curve Divider v6.0.2**, **Multiple Points v3.0.2**,
  **Creaser Adv v12.0.2**, **Combined Roof Tools v3.1.2**, **Roof Type Manager v3.1.2**, **Plan From Scope Box v6.1.2**,
  **Roof Shape From Link v2.0.1**, **Create Sections From Detail Lines v13.1.2**, **View Auto Renamer v6.1.2** (V006) and **v7.0.1** (V007).
  Older coexisting versions (e.g. Section Auto Renamer V025, View Auto Renamer V005) got the same fix without a version entry.

**User note:** no behaviour change beyond the fix.

## [6.5.0] — 2026-10-22

### Added
- **View Auto Renamer v7.0.0 (V007)** — opens much faster on large projects. It is the top entry in the **Auto Renamer**
  dropdown; V006 and V005 stay below it, unchanged.
  - Sheet placements for legends and schedules are found with two project-wide searches instead of two searches per sheet.
  - The views grid only builds the rows it shows when the window opens, instead of a row for every view in the project.
  - Errors now go through the plugin's standard error dialog and log (`%AppData%\Revit26_Plugin_Revised\Logs`).

**User note:** renaming, filters and settings work the same as V006 and share its settings file.

## [6.4.1] — 2026-10-22

### Changed
- **Detail Line Manager v1.1.1** — the **Line Manager** ribbon tooltip now describes the 1.1 features: deleting or joining
  overlapping lines, and seeing attached dimensions and tags before confirming a delete.

**User note:** tooltip text only; the button, its name and the tool's behaviour are unchanged.

## [6.4.0] — 2026-10-22

### Added
- **Detail Line Manager v1.1.0**
  - **Overlapping straight lines:** the search now also finds straight lines of the same style in the same view that lie on
    top of each other without being exact copies (flag "Overlaps", or "Covered by &lt;id&gt;" when a longer line covers one
    fully). Delete unused has a new option for them: **Delete covered lines** or **Join into one line** (the longest line is
    extended over the others, which are then deleted). "Show only duplicates / overlapping" includes them.
  - **Attached dimensions and tags are shown before deleting.** Delete checked / Delete all found first check the delete in
    Revit (rolled back, nothing changes) and the confirmation bar says what Revit would remove with the lines, e.g.
    "12 Dimensions, 3 Generic Annotations". Preview reports the same.
  - **Keep styles matching** (e.g. `A-*;S-*`): unused styles whose name matches are never deleted. A warning explains that
    Revit does not report styles used only by the Linework tool.
  - **Weight column shows the view's Visibility/Graphics weight** ("vg") for the line style (or Lines), read from the view
    template when it controls V/G. The tooltip shows style, V/G and element-override weights. View filters are not read.
  - **Group name** in the Flags column and a **Select groups** button.
  - **Borrow up front:** in workshared models each change first borrows all the elements it needs in one call and logs
    how many were borrowed and which are owned by other users (those are skipped), before anything changes.
  - **Progress and Cancel:** searches run in short steps with a progress count in the status strip and a **Cancel search**
    button; Revit stays usable during a long whole-project search. Cancelling keeps the previous results.
  - **Search when the window opens** setting (on by default).

### Changed
- **Detail Line Manager v1.1.0** — line-style usage across the project is counted only while the **Delete unused** tab is
  open (switching to it counts it), which makes searches from the Line thickness tab faster. The settings file is now
  format version 2; version 1 files load unchanged.
- Plugin `<Version>` set to 6.4.0 (the 6.3.1 bump had not reached the project file).

**User note:** existing options behave as before; the overlap option is off by default and searching on open stays on.
Before each change in a workshared model the tool now borrows the elements first, so elements owned by others are
listed and skipped before the change instead of during it.

## [6.3.1] — 2026-10-22

### Fixed
- **Detail Line Manager v1.0.1** — after a Line thickness change (assign style, change style line weight or per-view override),
  the active view is now redrawn straight away. Revit could leave it showing the old weights after a line-style weight change.
  The log also says whether Revit's **Thin Lines** mode is on: while it is, every line is drawn at weight 1 and the new
  weights do not show on screen.

**User note:** no change to what is saved in the model or to settings; only the redraw and the extra log lines are new.

## [6.3.0] — 2026-10-22

### Added
- **Detail Line Manager v1.0.0** (new tool, Detail Lines panel → **Line Manager**). A modeless window to find and
  clean up detail lines (lines, arcs, splines and ellipses; linked models and filled-region / sketch boundaries are left out):
  - **Scope:** Selected · Active view (default) · Views on sheets · Whole project.
  - **Advanced search:** text over line style, view name, element ID and comment (optional regular expression),
    line-style checklist, view type, line weight 1–16, length min/max (mm), curve type and workset.
  - **Show only:** duplicates / overlapping, shorter than a tolerance (default 5 mm), in groups, owned by other users.
  - **Results grid** (sortable): ID, line style, weight ("ovr" when a view override is set), view, view type, length (mm),
    curve, workset and flags, with live found / checked / duplicate / short / unused-style counts.
    **Select in Revit** and **Show element**.
  - **Delete unused:** unused line styles (checked against every curve in the project; built-in styles are never listed),
    duplicate / overlapping lines (keep oldest ID, newest ID or heaviest weight) and short lines. **Preview** is a dry run;
    **Delete checked** and **Delete all found** ask for confirmation in the window first.
  - **Line thickness:** assign a line style, change the line weight of the styles the lines use (project-wide, with a warning),
    or set / clear a per-view line weight override.
  - Lines in groups and elements owned by other users are skipped and logged. Each action is one Revit undo step and rolls
    back completely on any error. Filters and options are remembered in
    `%AppData%\Revit26_Plugin_Revised\DetailLineManager\settings.json` (with a `Version` field).

**User note:** new button only; existing tools and buttons are unchanged.

## [6.2.0] — 2026-10-22

### Added
- **Auto Slope By Point v32.3.0** — Circle markers → Highest point has a new option under the "Offset: X mm" text note:
  **Also add it at every roof boundary corner**. When ticked, each corner of the roof's outer boundary gets the same
  red "Offset: X mm" note with a leader, showing that corner's calculated elevation. Openings are not included,
  and no circle is drawn at the corners. A corner that already has the highest-point note is not labelled twice.
  The option is only available while the text note is ticked, and it needs a plan view like the other markers.

**User note:** the new option is off by default, so runs behave as before until you tick it. Existing settings load unchanged.

## [6.1.1] — 2026-10-22

### Changed
- **Auto Slope By Point v32.2.1** — the Circle markers section is more compact: each marker group is one row (on/off, line style, colour, radius) under a single column heading, with its note and options on the lines below.

**User note:** layout only; marker settings are unchanged.

## [6.1.0] — 2026-10-22

### Added
- **Auto Slope By Point v32.2.0** — "Longest 10 paths" now shows the elevation each path gives at 1 %, 1.5 % and 2 % slope (path length × slope, in mm).

### Changed
- **Auto Slope By Point v32.2.0** — the Longest 10 table drops VertexIndex, DrainIndex, ElevCalc_mm and ElevDiff_mm (narrower grid); "Copy rows" follows the same columns. The Excel workbook and the Skipped points table are unchanged.
- Labels: "N Drains Picked", "Drainage Distance (m)" (was "Max drainage distance (m)"), "Also use shape points nearby". The Drains, Slope (%) and drainage distance fields in "Every run" now sit on one row (label beside field), with their hints on a second row.
- The Every run fields and the four option sections are re-aligned on a fixed grid (labels in one column, controls at one height, hints below). The window is more compact: smaller default size (1000 × 640), tighter spacing, and the "Every run", options and log area is about 30 % shorter.

**User note:** display only; the slope calculation and the Excel export are unchanged.

## [6.0.1] — 2026-10-22

### Fixed
- **Roof Tag v18.1.2 and Roof Tag V017** — clicking OK or Cancel in the settings window threw
  "DialogResult can be set only after Window is created and shown as dialog" and rolled the tool back.
  The window is modeless, so the buttons now just close it.

## [6.0.0] — 2026-10-21

### Changed
- **Settings and logs now live in `%AppData%\Revit26_Plugin_Revised`** (and `Documents\Revit26_Plugin_Revised`), including `Logs`, instead of `Revit26_Plugin`.
- **Ribbon tabs renamed** to `SloperPro_Rev_88-MDL` and `SloperPro_Rev_88-DCM` (were `SloperPro-MDL_NT_88` and `SloperPro-DCM_NT_88`), so the Revised add-in sits beside the old one.
- **ToolGuard** treats the old `Revit26_Plugin` add-in as a separate plugin: it no longer reports it as a duplicate copy.

**User note:** existing settings and logs in the old `Revit26_Plugin` folder are not read. Tools start with default settings; copy the old tool folders across to keep them.

## [5.7.1] — 2026-10-21

### Changed
- **Auto Slope By Drain (Multi-Roof) v13.1.2** — the Longest 10 / Skipped results now sit below the
  Detected drains grid instead of beside it. Both are expanders: Detected drains is open and the
  results are closed by default.
  - **User note:** click the results header to open it. The drains grid now uses the full width right
    of the options column.

## [5.7.0] — 2026-10-21

### Changed
- **Roof Shape From Link v2.0.0 (V002)** — the final elevation now subtracts the host roof's thickness:
  `New Z = linked elevation − host roof type thickness − Subtract`. The point is then written as an
  offset from the host roof's base surface (base level elevation + Base Offset From Level), shown as
  **Offset from base**. New grid columns **Base Z**, **Thickness** and **Offset from base**, and a
  **Subtract host roof thickness** option (on by default). Thickness is the total of all layers of the
  roof type. Where a roof's base surface is sloped at a point, the measured base is used (a warning is
  logged) so the final elevations stay exact.
  - **User note:** results are lower than in V001 by the roof thickness. Untick **Subtract host roof
    thickness** to get the V001 result. Namespace is now `…RoofShapeFromLink.V002`; the ribbon button's
    internal name and the command class name are unchanged.

## [5.6.1] — 2026-10-21

### Fixed
- **Roof Shape From Link v1.1.1** — Compute no longer stops with "Cannot change or check the contents
  or Current position of CollectionView while Refresh is being deferred". The grid rows are now
  replaced without a deferred refresh.

## [5.6.0] — 2026-10-21

### Added
- **Isolated Section v1.0.0 (V001)** — new button **View Tools ▸ View Create ▸ Isolated Section**.
  Select model elements (or pick them from the window) and it makes **one 3D view** of them:
  - **Aligned to the largest side.** Each element's footprint is fitted with its smallest plan
    rectangle; the largest side is the biggest long side (length × height) of *any* selected element.
    The section box is turned in plan to be parallel to that side and holds every selected element.
  - **Or the side you choose:** long side A / B (back), short side C / D — each labelled with the
    compass direction it faces — or **Pick face…** to look straight at a face you click.
  - **Camera:** facing the side, level (default), or angled 3D (30° to the right, 35° above).
  - **Isolation:** temporary isolate (default — Revit resets it when the view is closed), permanent
    "hide everything else", or none (section box only).
  - **Linked elements:** **Pick in link…** picks elements inside loaded Revit links (the link
    transform is applied). Revit cannot isolate a single linked element, so the link instance is
    isolated and the section box hides the rest of it; the log says so.
  - 3D view type, view template, detail level, visual style, box margins (sides, top/bottom,
    front/back) and a view-name pattern (`{Category} {Id} {Count} {Side} {Date}`; a number is added
    when the name is taken). A plan preview in the window shows the elements, the box and the side.
  - One Undo step; anything that fails rolls the whole view back. Options are saved in
    `%AppData%\Revit26_Plugin\IsolatedSection\settings.json` (with a schema version).
- Unit tests for the tool's geometry (`dotnet test Tests/IsolatedSection.Tests`).

**User note:** temporary isolation lasts until the view is closed. Choose "Hide everything else
(permanent)" for a view you will put on a sheet.

## [5.5.0] — 2026-10-21

### Added — Shared Parameter Studio V001 (1.0.1)
A new **Shared Params** button on the Manage tab opens one window with four tabs. Existing tools,
including ParaManager, are unchanged.

- **Create & Assign.** Define a new shared parameter (name, data type, description, group in the file)
  and bind it to the categories you tick, as an instance or type parameter under a Properties group,
  in one step. The definition is written to the shared parameter file you choose (created if missing)
  and bound in a single undoable transaction. Names already in the file or the project are refused.
- **Merge Files.** Merge two shared parameter files into a third. GUIDs are never changed. Where both
  files define the same name or GUID differently, choose: keep the first, use the second, or keep both
  (the second is renamed). Preview first; new parameters can be added to the merged file in the same step.
- **Duplicate Check.** Finds repeated GUIDs and repeated names in a file, flags clashes with different
  data types, exports a CSV report and saves a copy without the later duplicates. The scanned file is
  never changed unless you pick it as the target (the old file is then kept as `.bak`).
- **Retrieve from Project.** Lists the shared parameters bound in the open project with GUID, type,
  binding and categories; write the ticked ones to a shared parameter file (GUIDs kept) or export a CSV.
  Project parameters that are not shared are counted, not listed.

User note: Revit's own shared parameter file setting is restored after each run. Files the tool
overwrites keep a `.bak` copy next to them.

Also in this release: a live name check on Create & Assign (red message as you type when the name is
invalid or already used), search boxes and sortable columns on Duplicate Check and Retrieve from Project
("Tick all" ticks only the shown rows), and an "Undo with Ctrl+Z" reminder after a successful create.

## [5.4.0] — 2026-10-21

### Changed — Roof Shape From Link V001 (1.1.0)
The Shape points grid now has the advanced-grid features.

- **Sort and resize.** Click a header to sort; drag a header edge to resize; reorder columns by dragging.
  The tick, Roof and Pt columns stay in place when you scroll sideways.
- **Search and filters.** A search box, a roof filter, and Status and Source filters. **Ticked only** hides
  unticked rows.
- **Group by roof** (on by default) with collapsible roof headers and a point count.
- **Status** column: OK, Snapped, Below or No link.
- **Δ vs current** column: the change each point will make, with a bar scaled to the largest change.
- A typed-in New Z is outlined so it stands out from calculated ones.
- **Columns** menu hides any column. **Compact rows** and **Reset view** are new.
- **Bulk bar** sets Source or Subtract, or ticks and unticks, for the rows that pass the filters.
  It replaces **Tick all** / **Tick none**, which now act on the visible rows.
- A summary line under the grid shows counts and the smallest, average and largest change.
- **Shape From Link** now also has a button in the **Roof Tools** panel (same tool, as in Shape Points).

User note: the old Tick all / Tick none buttons are now Tick / Untick in the bulk bar and only affect the
rows you can see. Clear the filters first to affect every row.

## [5.3.0] — 2026-10-21

### Added — Roof Shape From Link V001 (1.0.0)
A new tool in the **Shape Points** panel (button **Shape From Link**). It sets the shape-editing points of
footprint roofs from the roofs in a linked model.

- **Entry checks.** Works in a plan view only, and needs one or more footprint roofs selected before you run
  it (extrusion roofs and other elements are ignored). If either is missing, or the project has no loaded
  link, it warns and exits.
- **Linked files.** A searchable tick list of the loaded Revit links. Tick one or more.
- **Elevations.** For every shape-editing point of each selected roof the tool finds the linked roof above it
  and reads two elevations: the Z of its top (sky-facing) surface at the same X,Y (snapped to the face edge
  if the point sits within 0.5 m outside it), and the Z of its nearest shape-editing point. An option limits
  "nearest" to corner and edge points.
- **Editable grid.** Per point: Surface Z, Nearest Z, a Source choice (Surface by default), a Subtract value
  and the New Z, all editable. A global **Take elevation from** switch sets every row, and a global
  **Subtract** number (New Z = linked Z − number) fills every row's Subtract. Typing a New Z marks the row
  Manual. Points with no linked roof above start unticked so you can type a value or leave them out.
- **Change selection** lets you pick other footprint roofs without closing the window.
- **Apply** writes the ticked rows, one undo step, one transaction per roof (a roof that fails is rolled back
  on its own). Roofs owned by another user, or without shape editing on, are skipped and listed in the log.

User note: the tool never turns shape editing on; a roof needs existing shape-editing points.

## [5.2.0] — 2026-10-20

### Added — Tracker ID V002 (2.0.0)
A second Tracker ID tool next to V001. The **Tracker ID** drop-down on the Manage tab now lists
**Tracker ID V002** first and **Tracker ID V001** below it. V001 is unchanged.

- **Two pages.** *Elements & tracking* holds everything in collapsible sections (Scope, Categories &
  types, Result, Export, Status, Health); each section header shows its current choice. *Tracker ID
  naming* is only for deciding how new IDs are built.
- **Categories and types from drop-downs.** Each opens a searchable tick list with the old filters,
  counts and coverage; tick several categories and their types at once.
- **Scope:** whole model, current selection or active view.
- **Naming page:** the segment editor, separator and fallbacks, plus new **type codes** (a short code per
  type used by the Type part; blank = Type Mark as before), **upper case** (off by default) and a
  preview of the next new IDs.
- **Select & zoom** from the result, status and health grids (double-click a row).
- **Export** as CSV or Excel, and an append-only **status template** for SharePoint.
- **Status from SharePoint:** writes `Tracker_Status`, `Tracker_Progress` and `Tracker_StatusDate`
  into Revit in one undoable step, and colours the active view or a "Tracker - Status" 3D view.
- **Health check:** duplicates, copies, missing IDs, moved elements, far from grid, status gaps,
  deleted IDs; CSV report and "Fix in Result".
- **Deletion warning:** when elements with a Tracker_ID are deleted, Revit shows a warning (Ctrl+Z
  restores them) and their IDs are logged in `%AppData%\Revit26_Plugin\TrackerId_V002\Deleted`. The
  health check lists them under **Deleted** until the element is restored or the ID is cleaned up.
- **Large models:** elements are read in batches of 1,000 with a progress bar and **Cancel**, so Revit
  stays responsive.
- **Worksharing check:** before writing, the tool says who owns the elements it can't change, e.g.
  "owned by Ahmed (12), Sara (6)". Those are skipped; the rest are written.
- **First-run guide:** three short tips the first time V002 opens, and a **What is a Tracker_ID?**
  button to show them again.
- **Naming templates:** start the naming from Standard, Simple (Level-Type-Number), By zone, By grid or
  Full, then adjust.

**User note:** V001 and V002 read and write the same `Tracker_ID` parameters, so IDs are shared and you
can use either. V002 keeps its own settings (`%AppData%\Revit26_Plugin\TrackerId_V002`); the first time
it opens it starts from your V001 settings. The three status parameters are added only on the first
status import, with fixed GUIDs: `Tracker_Status` (Text, 15cff867-a1c5-4af3-bc47-21ebcde0be7e),
`Tracker_Progress` (Number, bdfb3eab-54a5-4895-bb1a-a54befce2835), `Tracker_StatusDate` (Text,
f33f9a95-3856-4c9c-a56c-f640e5ce5134). V002's CSV keeps V001's columns in the same order and appends
`Status`, `Progress_Pct`, `Status_Date` (`Schema_Version` 2). The V001 button's label now reads
"Tracker ID V001"; its internal name is unchanged.

## [5.1.0] — 2026-10-20

### Changed
- **Roof Points & Slope v1.1.0 (V001)** — redesigned window. The options no longer sit in one long
  page:
  - **Step list on the left**: Run options and steps 1–6, each with its Run tick, a number (navy
    when the step will run, grey when it won't), a one-line summary and the points it will add
    (ridge summary for step 5, drain count for step 6). These badges replace the six header tiles.
  - **Options of the selected step on the right**, laid out as label · value · unit, with the
    step's own **Run this step** tick. The **Openings** grid sits right under them, with its
    count and the Points / Perpendicular / Drain All–None buttons.
  - **Toolbar** above: roof picker (several roofs), presets, Show planned points / Clear markers.
  - **Run options** gathers Clear existing shape points first, Min spacing, the report folder and
    both Excel reports (the Auto Slope By Drain report option moved here from step 6).
  - **Activity log** is closed by default (click its title to open it), like the other tools.
  - Built from the shared styles only (`SharedStyles.xaml` v4.0 status and action brushes,
    26 px inputs, 30 px buttons, 8 px spacing); footer buttons are all the same height.

**User note:** nothing to migrate. Every option, preset and setting is the same as in 1.0.0 and
your saved `settings.json` and `presets.json` load unchanged; only where the options are shown
changed. Click a step in the list to find its options.

## [5.0.1] — 2026-10-20

### Fixed
- **Tool windows failing with "Cannot find resource named 'BrushSurfaceWindow'"** (or another
  style key) are now explained. The cause is a second, older copy of the plugin loaded in Revit:
  WPF can then load a window's styles from that copy. When Revit finishes starting, SloperPro now
  warns if another copy is loaded and shows its file path; the tool error dialog and the error log
  list the other copies too.

**User note:** if you see the warning, close Revit, delete the `.addin` file that loads the other
copy (in `C:\ProgramData\Autodesk\Revit\Addins\2026` or `%AppData%\Autodesk\Revit\Addins\2026`)
and its DLL, keep only `rf12_Revit_2026.addin`, and restart Revit.

## [5.0.0] — 2026-10-20

### Changed
- **The plugin DLL is now `Revit26_Plugin_Revised.dll`** (was `Revit26_Plugin.dll`):
  `<AssemblyName>` in the `.csproj` is `Revit26_Plugin_Revised`, so the build writes
  `Revit26_Plugin_Revised.dll` and `Revit26_Plugin_Revised.deps.json`.
- Every `pack://application:,,,/Revit26_Plugin;component/…` URI (shared styles, window resources,
  icons) now names `Revit26_Plugin_Revised`, including the windows added in 4.1.0–4.4.0 (Island Roof
  Splitter V002, Roof Points & Slope, Tracker ID…); with the old name the tool windows would fail to
  load their resources.
- `CLAUDE.md` now allows a namespace or assembly-name change when the owner asks for it, and lists
  everything such a change has to update. It also documents the `Revit26_Plugin_Revised` root
  namespace and the add-in manifest.
- Unchanged: the project file `Revit26_Plugin.csproj`, the add-in `AddInId`, and the
  `%AppData%\Revit26_Plugin` / `Documents\Revit26_Plugin` settings and log folders.

### Added
- **Add-in manifest in the repo: `Installer/rf12_Revit_2026.addin`** — loads
  `Revit26_Plugin_Revised.App` from `Rf_Revit_2026\Revit26_Plugin_Revised.dll` next to it, with the
  same file name, `AddInId` and `VendorId` as the manifest the installer writes. For a manual
  install, copy it to `%ProgramData%\Autodesk\Revit\Addins\2026\` and the build output to the
  `Rf_Revit_2026` subfolder there. The three installer scripts copy and register the new DLL and
  `.deps.json`.

**User note (breaking):** an `.addin` file whose `<Assembly>` still points at `Revit26_Plugin.dll`
loads the old build. Reinstall with the updated installer, or replace the `.addin` with
`Installer/rf12_Revit_2026.addin` and delete the old `Revit26_Plugin.dll` from the add-in folder.

## [4.4.0] — 2026-10-20

### Added
- **Roof Points & Slope v1.0.0 (V001)** — new tool in **Roof Slope ▸ Points & Slope**. One window
  runs up to six steps on one footprint roof, in order, as a single Undo step:
  1. **Points around openings** — per opening, N points (circles / others defaults, editable per
     row) or a max spacing. Same rules as Inner Loop Divider V012.
  2. **Perpendicular points** — from each group of points on an opening to the nearest boundary
     point in up to 8 directions. Same rules as Inner Loops + Perpendicular V007.
  3. **Midpoint and quarter points** — ½ and/or ¼ + ¾ of each edge, as Multiple Points V003.
  4. **Gap fill** (new) — along each edge, evenly spaced points wherever two neighbouring points
     are further apart than the interval.
  5. **Ridge between drains** — the Ridge By Openings V070 services, unchanged: ridges midway
     between the openings ticked as drains (at least two), clipped to the roof, as shape points +
     split lines and/or detail lines in the active view (line style chosen in the window).
     Openings that are not drains end the ridges that cross them.
  6. **Slope by drain** — the Auto Slope By Drain V013 engine, unchanged, towards the same
     drains, so the ridges become the high lines (roof parameters and the optional Excel report
     included).
  Steps 3 and 4 each choose their edges: boundary and/or openings, lines and/or curves. The header
  shows how many points each step will add (and the ridge lines) before anything runs; points already there are skipped.
  Before running, the window also:
  - **Check slope reach** (step 6) — estimates how many points the slope would leave at 0 (dips):
    no on-roof path to a drain within Max edge hops, or a path longer than Max path.
  - Shows **ridge problems** — ridge points not midway between their drains, and ridge points that
    fall within the minimum spacing of another point.
  - **Min spacing between points** (default 50 mm) — steps 2–4 skip a point closer than this to
    another point; the skipped count is shown. Step 1 (counts you set per opening) is never thinned.
  - **Drain checks** — openings the ridge reader and the drain detection don't agree on are listed.
  - Warns when the roof **already has slope or extra points**, and asks before running on a roof
    that already has slope. **Clear existing shape points first** (off by default) resets the roof's
    shape before step 1, inside the same Undo.
  - **Cancel Run** — stops the run between steps or during the slope; everything is rolled back.
  - **Show in Revit** — "Show planned points in Revit" puts coloured temporary markers (one colour
    per step) in the active view; each opening row has a **Show** button that highlights the opening
    and zooms to it. Markers are Revit in-canvas graphics: not saved, not on the Undo list, cleared
    on Run or Close (or with **Clear markers**).
  - **Presets** — save the current options under a name, apply or delete them later
    (`%AppData%\Revit26_Plugin\RoofPointsAndSlope\presets.json`, with a schema version).
  - **Several roofs** — select or pick several footprint roofs. A **Roof** list switches between
    them; each roof keeps its own opening ticks and counts, the step options are shared. **Run on
    All Roofs** runs them in order as one Undo step; if one fails, all are rolled back. A roof that
    can't be processed (in a group, owned by another user, no closed sketch) is skipped with the
    reason in the log.
  - **Run report** (off by default) — after a successful run, a `.xlsx` in the report folder with a
    Summary sheet and a Points sheet (Roof, Step, X (m), Y (m), Height (mm)) listing every shape
    point and the step that added it.
  If any step fails, the whole run is rolled back. Settings are saved to
  `%AppData%\Revit26_Plugin\RoofPointsAndSlope\settings.json` (with a schema version).
  Internal names: `Btn_RoofPointsAndSlope_V001`, `Pulldown_RoofPointsAndSlope`.

Unit tests for the plan geometry, the point planner and the reach estimate are in
`Tests/RoofPointsAndSlope.Tests` (`dotnet test Tests/RoofPointsAndSlope.Tests`).

**User note:** nothing changes for existing tools. Combined Roof Tools V003 and the separate
point and slope tools are unchanged and stay on the ribbon. Footprint roofs only (the openings
are read from the roof's sketch). Not yet tested in Revit: try it on a copy of a model first.

### Fixed
- **Ridge By Openings v70.1.1 (V070)** — added shape points no longer distort the roof or the slope
  worked out from them (also used by step 5 of Roof Points & Slope):
  - Each ridge point is placed on the roof's current surface. It was placed at the most common
    height of the existing points, which dented or lifted a roof that was already sloped.
  - A point Revit rejects is nudged at most 60 mm. It was moved up to half way to the roof centre
    (metres off the ridge), or snapped to the nearest edge.
  - Split lines now run along each ridge, through its points. They were drawn between each point's
    nearest left and right vertices, creasing the roof where there is no ridge.
  - A ridge point that already exists is reused instead of being added again next to it; skipped
    and rejected points are now reported in the log.

**User note:** ridges made with V070 before this fix may have dents, displaced ridge points or stray
creases; reset the shape points and run the tool again.

## [4.3.0] — 2026-10-20

### Added
- **Tracker ID 1.0.0** (`Menu/Manage/TrackerId_V001`, button **Manage ▸ Tracker ID**). Gives every
  placed element of a chosen category and types one permanent `Tracker_ID`, for progress and
  material tracking in Power BI. Four steps:
  1. **Category** — every model category with its types, elements and Tracker_ID coverage; search,
     discipline chips (Architecture / Structure / MEP) and column filters.
  2. **Types** — the category's placed types with their existing-ID status; tick the ones to process.
  3. **Tracker ID** — existing IDs are read and never changed. Missing IDs are built from eight
     segments the user can switch off and reorder: Model (code of the Revit file, from Project
     Information ▸ *Model Code*, else the discipline code in the file name such as ARC, STR or MEP),
     Zone (Zone parameter, else the scope box around the element), Level, Type, Grid (nearest grid
     crossing to the element's centre point) and three custom segments (fixed text, a parameter value
     or a running number). Default format: `NT02-ARC-ZA-L01-D09-C04-0001`. New IDs are unique across
     the whole model, compared without regard to case, and the Model segment keeps them unique across
     models. Copy-pasted elements, which carry another
     element's ID, are found and get a new ID; moved elements keep theirs. Written in one
     transaction, so one Undo reverts it.
  4. **Export** — a CSV with fixed columns (`Tracker_ID, Category, Family, Type, Type_Mark, Zone,
     Level, Grid, Count, Length_m, Area_m2, Volume_m3, Revit_ElementId, Revit_UniqueId, Model,
     Export_Date, Schema_Version, Model_Code`) for Power BI, with an optional dated copy.
- **Shared parameters** (created only if missing, Text, instance, group *Identity Data*, allowed to
  vary between group instances): `Tracker_ID` (7afba1b3-3c36-40c4-b622-104fa374d700),
  `Tracker_Zone` (00d22852-1eff-4137-aadb-5f6b94757db4), `Tracker_Level`
  (eaa79360-4694-4488-8594-17d487241e99), `Tracker_Grid` (4a010dbe-ac6c-4971-8955-22b12e4a0bea),
  `Tracker_SourceGuid` (b306dc6d-1058-4ca6-ae0a-5b3e67ed40aa). An existing parameter of the same
  name is reused and only gets the category added to its binding.

**User note:** give each Revit model its own short code in Project Information ▸ *Model Code*
(ARC, STR, MEP…) if its file name has no discipline code; the tool warns when it has to guess one.
The ID format and export path are remembered in
`%AppData%\Revit26_Plugin\TrackerId\settings.json`. Never edit `Tracker_ID` by hand: Power BI
history is keyed on it. Keep material and progress status in your Excel table on SharePoint, keyed
by `Tracker_ID`, and relate it to the exported CSV in Power BI.

## [4.2.1] — 2026-10-19

### Changed
- **Panel order** — panels now follow the workflow. SloperPro-MDL: Roof Slope, Shape Points,
  Roof Tools, Roof Edit, Plan View, Compare, Insulation, Detail Lines, **Utilities** (moved from
  first to last). SloperPro-DCM: View Create, View Place, View Rename, **Sheet Create**,
  **DWG Tools**, **Family Tools**, Manage, Worksets. No button was renamed; only panel positions
  changed.
- **Link Check** — moved from its own one-button **Links** panel into the **Manage** panel (the
  Links panel is gone). Same dropdown, same internal name `Pulldown_LinkVersionChecker`.

### Fixed
- **Buttons that could only raise an error** — removed from the ribbon because their command
  source is not in the repository:
  - Roof Slope ▸ By Point: V031, V029, Ridge V002, Multi Copies 29 (V032, V030, Ridge V003 and
    Multi Copies 30 remain).
  - DWG Tools ▸ Export DWG: V001 (V004 and V003 remain).
  - Manage ▸ Overlaps V003, ParaManager V004.
  - Worksets ▸ WS Renamer V004, WS Renamer XL FX04.

**User note:** Utilities is now at the right-hand end of SloperPro-MDL_NT_02, and Link Check is in
the Manage panel. The removed entries never worked in this build, so no working tool was lost.

## [4.2.0] — 2026-10-19

### Added
- **Plan View panel** on the SloperPro-Modelling tab, with a direct **Island Splitter** button that
  runs Island Roof Splitter V002 (2.0.0). It is the same command as the V002 entry in **Roof Edit ›
  Island Splitter**, which is unchanged.

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
