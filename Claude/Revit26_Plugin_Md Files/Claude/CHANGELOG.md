# Changelog

Tracks *why* a tool's live version on the ribbon changed, or a cross-tool
convention shifted — not a mirror of `git log`. Add an entry here whenever:

- a new `_V0xx` becomes the one wired into a `*Ribbon.cs` file (see
  `Claude/TOOL_VERSIONING.md`), or an old one is retired,
- a convention documented in `CLAUDE.md`, `Claude/ARCHITECTURE.md`, or
  `Claude/RIBBON_AND_ICONS.md` changes,
- a fix crosses more than one tool (shared parameters, shared services).

Routine single-tool bug fixes with no version bump don't need an entry —
the commit message is enough for those. Newest first.

Commit history before 2026-09-09 is terse/inconsistent ("Updated", "DOne",
"V020") and isn't reliably summarizable — use `git log` directly for that
period rather than trusting a reconstruction here.

**The release log is the root `CHANGELOG.md`.** It has every plugin release
and tool version, with user notes. This file is for engineering context only.
No entries were added between 2026-10-17 and 2026-11-02; for that period
(plugin 3.x to 6.30.x) read the root `CHANGELOG.md` and `git log`.

---


## 2026-11-02 — Floor Slope tools, UI previews, docs refresh (plugin 6.31.0)

- **New tools, not new versions:** Floor Slope By Drain V001 and Floor Slope By
  Point V001 are copies of Auto Slope By Drain (Multi-Roof) V013 and Auto Slope
  By Point V033, retargeted from `RoofBase` / `FootPrintRoof` to `Floor`. They
  got their own folders under `Menu/FloorTools/FloorSlope/`, their own
  `ToolCatalog` entries and their own settings folders. The roof tools are
  untouched. A copy was chosen over a shared roof/floor engine so neither roof
  tool's behaviour could change.
- **Floor-only logic is shared** in `Shared/Services/FloorShapeEditChecks.cs`:
  - shape-edit support, group, read-only and borrowed-by-another-user checks;
  - the "no Variable layer" Continue / Skip dialog. It is asked each time, per
    the owner's choice.
- **Drain families:** By Drain also treats Plumbing Fixtures on the floor as
  drains, per the owner's choice. Only names that look like drains start
  ticked, so WCs and basins are listed but unused.
- **Ribbon:** a new **Floor Slope** panel with two plain buttons, added after
  Roof Slope by `FloorToolsRibbon.BuildSlopePanel`. It is its own panel at the
  owner's request, despite the "add to an existing panel" guidance.
- **Convention: UI previews.** The root `CLAUDE.md` now requires an HTML
  preview (`Claude/Previews/<ToolFolder>_Window.html`) for every tool update,
  published as a claude.ai Artifact. Its link goes in the file header comment
  and the PR description.
- **Docs refresh:** this folder's `CLAUDE.md`, `ARCHITECTURE.md`,
  `RIBBON_AND_ICONS.md`, `TOOL_VERSIONING.md` and `UI_UX_PATTERNS.md` were
  rewritten against the current code. Corrections:
  - namespaces are now `Revit26_Plugin_Revised`;
  - there are two ribbon tabs, and no `QuickAcces.cs`;
  - 12 test projects exist;
  - most `Core/` folders do use the Revit API;
  - tools change in place since 3.10.0;
  - the full current panel layout is recorded.

## 2026-10-17 — Manage / Links

- FileStatusChecker V002 — picks up the two 2026-10-04 conventions V001 shipped
  without. The log panel now starts **collapsed**: the header row and its Copy
  All / Copy Selected / Export / Clear stay visible, and the title toggles the
  lines. The footer `Rescan` moves to the red `RunButton`; Tab 2's `Search`
  stays navy, being a body action rather than the footer action.
- New `IsLogExpanded` on the ViewModel and in `settings.json`, so the collapsed
  or expanded choice survives a reopen. The log title bar is fully rounded while
  collapsed so no dark lip shows beneath it.
- No behaviour change: every binding, command, transaction and Revit-side call
  is V001's. Namespace `Revit26_Plugin.FileStatusChecker.V002`; V001's folder
  kept on disk per `Claude/TOOL_VERSIONING.md`.
- Ribbon: **not yet wired.** The Manage pulldown still points at V001 only —
  V002 is compiled-but-orphaned until the `ManageRibbon.cs` edit lands
  (V001 first, V002 second).
- Note: `settings.json` and the snapshot store are keyed by tool name, not
  version, so V001 and V002 share both.

## 2026-10-04 — ToolWindowShell (CLAUDE.md v1.1)

- Shared: new `Shared/Controls/ToolWindowShell` (header / body / footer;
  the body is the window's only ScrollViewer; footer status strip bound to
  `IsRunning` / `Progress` / `SummaryText`) and `ShellBehaviors`
  (`MaxHeightRatio`, `ForwardMouseWheel`). SharedStyles 3.1 adds `Tile*`
  brushes and `MetricTile*` styles.
- SectionAutoRenamer V026 — first tool on the shell (reference
  implementation): log moved from footer into the body, grid and log
  height-capped, V024's tinted metric tiles restored via named brushes.
  ViewModel gains `IsRunning` / `Progress` / `SummaryText`; no behaviour
  change.
- Ribbon: Section Renamer pulldown is now V025, V026 (V024 removed from the
  ribbon; folder kept).
- DetailLineClosedLoop V003 on the shell: Selection/Options left (own
  ScrollViewer removed), Created Lines grid right, Processing Log moved
  from footer into the body. Shell binds to the existing `IsBusy` /
  `RunSummary`; ViewModel gains `Progress` only. Ribbon pulldown: V002,
  V003 (V001 removed from the ribbon; folder kept).
- DetailLineDimensions (DtlLineDim) V010 on the shell: Selection,
  Dimension Settings and Activity Log (moved from footer) in the body; the
  middle's own ScrollViewer removed. Binds to `IsBusy` / `RunSummary`;
  ViewModel gains `Progress` only. Ribbon pulldown: V009, V010 (V008
  removed from the ribbon; folder kept).
- Shell: an empty `Subtitle` now collapses instead of leaving a blank line.
- OuterCurveDivider V006 on the shell: type-rules grid, edges grid and
  Activity Log (moved from footer) in the body; both grids and the log use
  MaxHeightRatio (type grid's fixed MaxHeight=160 replaced). ViewModel
  gains `IsRunning` / `Progress` / `SummaryText` (not yet set, so the
  status strip stays empty). Ribbon pulldown: V005, V006 (V004 removed
  from the ribbon; folder kept).
- VertexReducer (RoofEdgeVertexReducer) V009 on the shell: Selection,
  Settings, Preview Results grid and Log (moved from footer) in the body;
  the old footer summary line is now the shell status strip
  (`SummaryText`). ViewModel gains `IsRunning` / `Progress`. Ribbon
  pulldown: V008, V009 (V007 removed from the ribbon; folder kept).
- MultiplePoints V003 on the shell: Points To Add settings, edges grid and
  Activity Log (moved from footer) in the body. ViewModel gains
  `IsRunning` / `Progress` / `SummaryText` (not yet set). Ribbon pulldown:
  V002, V003 (V001 removed from the ribbon; folder kept).
- RoofDetailLineIntersect V014 on the shell: Selection, Options and Run Log
  (moved from footer) in the body; the middle's own ScrollViewer removed.
  Binds to the existing `IsBusy`; ViewModel gains `Progress` /
  `SummaryText` (not yet set). Ribbon pulldown: V013, V014 (V012 removed
  from the ribbon; folder kept).
- CreaserAdv V012 on the shell: detail item, filters, minimum slope and Log
  (moved from footer) in the body; the middle's own ScrollViewer removed.
  Binds to the existing `IsRunning`; ViewModel gains `Progress` /
  `SummaryText` (not yet set). Ribbon pulldown: V011, V012 (V010 removed
  from the ribbon; folder kept).
- RoofTag V018 on the shell: warning banner, settings cards and Log (moved
  from footer) in the body; the middle's own ScrollViewer removed. Still a
  modal OK / Cancel dialog. ViewModel gains `IsRunning` / `Progress` /
  `SummaryText` (not yet set). Ribbon pulldown: V017, V018 (V016 removed
  from the ribbon; folder kept).
- CompareRoofs (RoofPointElevationSync) V005 on the shell: selection card,
  point-mapping grid and Log (moved from footer) in the body; the old
  footer summary is now the status strip. Binds to `IsBusy` /
  `SummaryText`; ViewModel gains `Progress`. Ribbon pulldown: V003, V004,
  V005 (V002 removed from the ribbon; folder kept).
- RoofPointComparison V003 on the shell: both metrics rows in the header;
  tolerances, marker settings and Processing Log (moved from footer) in
  the body; the middle's own ScrollViewer removed. Binds to `IsBusy` /
  `StatusMessage`; ViewModel gains `Progress`. Ribbon pulldown: V002, V003
  (V001 removed from the ribbon; folder kept).
- Shell: new `IsIndeterminate` option — the status-strip bar animates for
  tools that only have a busy flag, no percentage.
- AnnotationOverlapDetection V004 on the shell: type list and results grid
  in the body (both capped; type list's fixed MaxHeight=150 replaced); the
  old footer busy bar is now the shell's indeterminate bar on `IsLoading`.
  No log in this tool. ViewModel gains `Progress` / `SummaryText`. Ribbon
  pulldown: V003, V004 (V002 removed from the ribbon; folder kept).
- WorksetsElementsBrowser WSEB004 on the shell: toolbar and workset tree
  (capped) in the body; busy overlay kept over the shell; status strip is
  indeterminate on `IsBusy`. No log in this tool; View3DPickerWindow
  unchanged. ViewModel gains `Progress`. Ribbon pulldown: WSEB003, WSEB004
  (WSEB002 removed from the ribbon; folder kept).
- BubbleAutoRenumber V008 on the shell: sheet + options, sections grid and
  Log (moved from footer) in the body; grid and log TextBox capped.
  ViewModel gains `IsRunning` / `Progress` / `SummaryText` (not yet set).
  Ribbon pulldown: V007, V008 (V006 removed from the ribbon; folder kept).
- DwgToLines V007 on the shell: CAD picker, placement/spline options and
  Activity Log (moved from footer) in the body; the middle's own
  ScrollViewer removed. ViewModel gains `IsRunning` / `Progress` /
  `SummaryText` (not yet set). Ribbon pulldown: V006, V007 (V005 removed
  from the ribbon; folder kept).
- DwgToDetailLines V013 on the shell: context banner in the header; options
  (own ScrollViewer removed), Layers & Hatches grid and Activity Log (moved
  from footer) in the body. Indeterminate status strip on `IsRunning`;
  ViewModel gains `Progress` / `SummaryText`. Ribbon pulldown: V011, V012,
  V013 (V002 removed from the ribbon; folder kept).
- RoofViewFocus V004 on the shell: Target, Crop Margins and Log (moved
  from footer) in the body; the middle's ScrollViewer and the Roofs list's
  own ScrollViewer removed. The green summary line under the log is now
  the status strip (`SummaryText`), indeterminate on `IsBusy`. ViewModel
  gains `Progress`; tool title constant bumped to V004. Ribbon pulldown:
  V002, V003, V004 (V001 removed from the ribbon; folder kept).
- Earlier migrations whose busy flag has no percentage (DetailLineClosedLoop
  V003, DtlLineDim V010, RoofDetailLineIntersect V014, CreaserAdv V012,
  CompareRoofs V005, RoofPointComparison V003) now set
  `IsIndeterminate="True"` so the status-strip bar animates while running.
- SharedStyles: `BrushRowSuccessBackground` / `BrushRowDangerBackground`
  (pass/fail row tints for result grids), replacing inline `#F5FFF8` /
  `#FFF5F5`.
- RoofRidgeLines (By Points) V059 on the shell: selection, settings,
  validation grid and Pipeline Log (moved from footer) in the body; footer
  status line + small progress bar replaced by the shell status strip
  (indeterminate on `IsBusy`, `StatusMessage`). ViewModel gains
  `Progress`. Ribbon: V057 replaced by V059 in the Ridge Lines pulldown.
- RoofRidgeLines (By Openings) V070 on the shell: inputs and drainage
  seeds (both own ScrollViewers removed; Circles expander open,
  Rectangles / Others closed, unchanged), validation grid and Pipeline
  Log (moved from footer) in the body; row tints use the shared row
  brushes. ViewModel gains `Progress`; version labels bumped to V070.
  Ribbon: V068 replaced by V070 in the Ridge Lines pulldown.
- InnerLoopDivider V011 on the shell: division control, toolbar, grouped
  loops grid (group expanders unchanged: Circular open, others closed) and
  Activity Log (moved from footer) in the body. Group-header inline colours
  replaced with existing SharedStyles brushes. ViewModel gains `IsRunning`
  / `Progress` / `SummaryText` (not yet set). Ribbon pulldown: V010, V011
  (V009 removed from the ribbon; folder kept).
- InnerLoopsAndPerpendicular V007 on the shell: toolbar, shapes grid (group
  expanders unchanged), tolerance, perpendicular-points grid and Activity
  Log (moved from footer) in the body; both grids capped. ViewModel gains
  `IsRunning` / `Progress` / `SummaryText` (not yet set). Ribbon pulldown:
  V006, V007 (V005 removed from the ribbon; folder kept).
- RoofFromDetailLines V009 on the shell: settings and ID outputs (own
  ScrollViewer removed), Roof Loops Preview expander (open on launch,
  unchanged) and Log (moved from footer) in the body; footer summary line
  is now the status strip (indeterminate on `IsBusy`). ViewModel gains
  `Progress`. Ribbon pulldown: V008, V009 (V007 removed from the ribbon;
  folder kept).
- AutoSlope By Point V030, By Point (Ridge) V003 and By Point Multi Copies
  MultiCopies30 on the shell (same layout): both metrics rows in the
  header; inputs (Circle Markers expander closed by default, unchanged)
  and curve/export column with their own ScrollViewers removed; Processing
  Log moved from footer into the body. The footer progress strip is now
  the shell status strip, bound to the existing `IsProgressVisible` /
  `ProgressPercent` / `ProgressIsIndeterminate` / `ProgressPhaseText` — no
  ViewModel changes. Ribbon By Point pulldown: V029, V030, Ridge V002,
  Ridge V003, MultiCopies29, MultiCopies30 (V028, Ridge V001 and
  MultiCopies28 removed from the ribbon; folders kept).
- AutoSlopeByDrain (Multi-Roof) V012 on the shell: settings column (own
  ScrollViewer removed; Circle Markers expander unchanged), per-roof drain
  tabs (drain-group expanders unchanged) and Processing Log (moved from
  footer) in the body. Footer progress strip replaced by the shell status
  strip on the existing progress properties — no ViewModel changes.
  Ribbon By Drain pulldown: V007, V011, V012 (V010 removed from the
  ribbon; folder kept).
- FloorsAndRoofFromLinkedRoomsViaPlanView V006 on the shell: inputs (own
  ScrollViewer removed), room list (capped), floor/roof summaries, toast
  and Activity Log (all moved from footer) in the body. Footer progress
  bar + text replaced by the shell status strip: `IsBusy`, `ProgressText`
  and a new read-only `Progress` (Processed ÷ Total). Ribbon From Rooms
  pulldown: V011, V012, ViaPlanView V005, V006 (ViaPlanView V004 removed
  from the ribbon; folder kept).
- WorksetRenamer V005 on the shell: toolbar card, selection row and
  worksets grid (capped) in the body; footer status line is now the status
  strip (`StatusMessage`). No log in this tool. ViewModel gains `IsRunning`
  / `Progress`. Ribbon pulldown: V004, V005 (V003 removed from the ribbon;
  folder kept).
- SharedStyles: result-group palette `BrushGroup{Ok,Warning,New,Neutral,
  Danger}{Accent,Background,Text}` (from WorksetRenamer FX04's inline
  colours).
- WorksetRenamer FX05 on the shell: export/import cards, filter bar and the
  five grouped result expanders (own ScrollViewer removed; OK open, others
  closed — unchanged) in the body; group grids capped via the shared grid
  style; footer status line is now the status strip (`StatusMessage`). No
  log in this tool. ViewModel gains `IsRunning` / `Progress`. Ribbon
  pulldown: FX04, FX05 (FX03 removed from the ribbon; folder kept).
- ParaManager V005 on the shell: metrics bar + step indicator in the
  header; current step (outer ScrollViewer and the parameter list's own
  ScrollViewer removed) and the docked, collapsible Activity Log in the
  body; footer status line is now the status strip (`SummaryLine`,
  indeterminate on `IsRunning`). Queue grid capped. ViewModel gains
  `Progress`. Ribbon pulldown: V004, V005 (V003 removed from the ribbon,
  with its now-unused tooltip const; folder kept).
- SharedStyles: `BrushTint{Success,Info,Warning}` faint tile tints (from
  WorksetManager V013's inline colours).
- WorksetManager V014 on the shell: metrics strip in the header; pattern
  toolbar, the three section grids (each capped — they now size to content
  instead of splitting the window height) and the Live Log (collapsed by
  default, unchanged) in the body. ViewModel gains `IsRunning` /
  `Progress` / `SummaryText` (not yet set). Ribbon pulldown: V013, V014
  (V012 removed from the ribbon; folder kept).
- SharedStyles: `BrushAction{Export,Import,Text}`, `BrushPill*` status-pill
  colours and `BrushBannerWarning*` (from ScheduleExportImport V006's
  inline colours).
- ScheduleExportImport V007 on the shell — both windows. Main: Refresh +
  summary cards in the header; actions, schedule list and Log (moved from
  footer) in the body; status strip on `IsBusy` / `StatusMessage`. Import
  Preview: warning banner + cards in the header; chips, search and preview
  grid in the body; same status strip. Grids and log capped. ViewModel
  gains `Progress`. Ribbon pulldown: V005, V006, V007, V004, V003 (V002
  removed from the ribbon; folder kept).
- SharedStyles: `BrushPill{New,Dup,Renamed}{Background,Text}` and
  `BrushDetail{Muted,Value}Text` (from RoofTypeCreator V002's inline
  colours).
- RoofTypeCreator V003 on the shell. Header: metric cards. Body: Export /
  Import tabs (both grids capped), Operation Log (moved from the footer)
  with its completion bar. Footer: status strip on `IsBusy`
  (indeterminate) + Close. ViewModel gains `Progress` / `SummaryText`
  (not yet set). Ribbon pulldown: V002, V003 (V001 removed from the
  ribbon; folder kept).
- SharedStyles: `BrushSoft{Success,Warning,Danger}Background` (from
  CreateSections V012's status pills).
- CreateSections V013 on the shell. Header: metric cards. Body: settings
  (left pane's own ScrollViewer removed; View Crop still collapsed) /
  Sections To Create grid (capped), then the Live Log (moved from the
  footer; still open). Footer: status strip on `IsRunning`
  (indeterminate) + Create / Close. ViewModel gains `Progress` /
  `SummaryText` (not yet set). Ribbon pulldown: V012, V013 (V011 removed
  from the ribbon; folder kept).
- SharedStyles: `BrushBadgeOnDark` (#33FFFFFF, from RoofEdgeAroundSections
  V006's naming chips).
- RoofEdgeAroundSections V007 on the shell. Header: metric cards. Body:
  parameters + naming (own ScrollViewer removed), Planned Sections grid
  (capped), log (moved from the footer). Footer: status strip on `IsBusy`
  (indeterminate) / `LastRunSummary` + Run / Close. ViewModel gains
  `Progress`. Ribbon pulldown: V006, V007 (V005 removed from the ribbon;
  folder kept).
- ViewAutoRenamer V006 on the shell. Header: status badges + metric cards.
  Body: filter bar + popover, rename panel, toolbar, quick-filter chips,
  views grid (capped), log (moved from the footer). Footer: status strip
  + Reset / dry-run badge / Commit / Close. The filter popover keeps its
  own ScrollViewer (a Popup is a separate window). Popover shadow drops
  its inline `#000000` (DropShadowEffect default is black). ViewModel
  gains `IsRunning` / `Progress` / `SummaryText` (not yet set). Ribbon
  pulldown: V005, V006 (V004 removed from the ribbon; folder kept).
- LinkedDetailLineGenerator VA009 on the shell. Header: boundary line +
  "Settings loaded" (was the title-bar subtitle) and metrics. Body: scope
  toggle, sections 1 / 2a / 2b / 2c (Expanders keep their ViewModel-bound
  state; their three MaxHeight-260 ScrollViewers removed), 3. Mapping,
  scope line, Processing Log (moved from the footer, outside the locked
  form). Footer: status strip on `IsRunning` (indeterminate) + Ready
  indicator, Clear / Create / Reset / Close. ViewModel gains `Progress` /
  `SummaryText` (not yet set); `GeneratorVersion` = "VA009". Ribbon
  pulldown: VA007, VA008, VA009, VA006 (VA003 removed from the ribbon;
  folder kept).
- CombinedRoofTools V003 on the shell. Header: Working Roof card + Change
  Roof. Body: the five tool tabs (each tab's log and actions stay inside
  its tab, in the body). Footer: status strip on `IsBusy` (indeterminate)
  / `RunAllStatusMessage` + Run All / Close. Tab views: own
  ScrollViewers removed (Auto Slope By Drain settings, Creaser Adv
  settings), every grid and log capped; Inner Loop Divider's inline
  group-header colours → existing SharedStyles brushes. ViewModel gains
  `Progress`. Ribbon pulldown: V002, V003 (V001 removed from the ribbon;
  folder kept).
- RoofEdgeElementSections V004 on the shell. Header: selection summary.
  Body: roofs / linked elements / parameters / naming (own ScrollViewer
  removed; Expander defaults unchanged), Planned Sections grid, log
  (moved from the footer); both grids capped. Footer: status strip on
  `IsBusy` (indeterminate) / `LastRunSummary` + Run / Close. Category
  filter popup keeps its ScrollViewer. Inline `#14283F` shadow →
  `NavyDark`, `#33FFFFFF` → `BrushBadgeOnDark`. ViewModel gains
  `Progress`. Ribbon pulldown: V002, V003, V004 (V001 removed from the
  ribbon; folder kept).
- SharedStyles: `BrushPill{Taggable,NotTaggable}Background` (from
  SectionViewAutoTagger V005's category pills).
- SectionViewAutoTagger V006 on the shell (folder
  `SectionViewAutoTagger_V006/SectionViewAutoTagger_V006`). Header:
  metric cards. Body: sheet / views / categories / worklist (left) and
  settings (right) — column ScrollViewers and the category list's
  ScrollViewer + 200 px cap removed — then the log (moved from the
  footer, capped). Section Views popover keeps its ScrollViewer. Footer:
  status strip on `IsRunning` (indeterminate) / `SummaryLine` + saved
  log path + Run / Close. ViewModel gains `Progress`. Ribbon pulldown:
  V005, V006 (V004 removed from the ribbon; folder kept).
- APUS V324 on the shell. Header: metric cards. Body: sections list
  (grid capped) / layout settings (own ScrollViewer removed), then the log
  (moved from the footer). Footer: status strip bound to existing
  `ShowProgress` / `Progress.Percentage` / `StatusMessage` (replaces the
  footer progress bar; current-operation line kept) + Place / Cancel /
  Close. No ViewModel change. Ribbon pulldown: V323, V324 (V322 removed
  from the ribbon; folder kept).
- SharedStyles: `BrushPillPlaced{Background,Text}` (from CalloutCOP V020's
  placed pill).
- CalloutCOP V021 on the shell. Header: summary metric cards. Body:
  configuration card + view grid (capped), log (moved from the footer).
  Footer: status strip on `IsRunning` (indeterminate) / `ProgressText`
  (replaces the footer progress line) + run-disabled reason, Place /
  Close. Drafting-view combo popup keeps its ScrollViewer. ViewModel gains
  `Progress`. Ribbon pulldown: V020, V021 (V019 removed from the ribbon;
  folder kept).
- RefSectionHeadPlacer V015 on the shell. Header: metric cards. Body:
  setup (left) / element data + mapping (right) — both panel
  ScrollViewers removed; the four grids' fixed MaxHeights become
  MaxHeightRatio caps — then the activity log (moved from the footer).
  Footer: status strip bound to existing `IsRunning` / `ProgressPercent`
  / `ProgressStatusText` (replaces the footer progress block) + Cancel /
  Run / Close. No ViewModel change. Ribbon pulldown: V014, V015 (V013
  removed from the ribbon; folder kept).
- SharedStyles: `BrushRowUnmappedBackground` (from
  FloorsAndRoofFromLinkedRooms V012's unmapped-level cell).
- FloorsAndRoofFromLinkedRooms V013 on the shell. Header: metrics card.
  Body: room grid (capped) / options pane (own ScrollViewer removed),
  floor/roof summaries + toast and activity log (both moved from the
  footer). Footer: status strip on `IsBusy` / computed `Progress` /
  `ProgressText` (replaces the footer progress bar) + Cancel Run, Create
  Floors / Create Roof / Close. ViewModel gains computed `Progress`.
  Ribbon pulldown: V012, V013, PlanView V005, PlanView V006 (V011 removed
  from the ribbon; folder kept).
- PlanFromScopeBox V006 on the shell. Header: metric cards. Body:
  scope-box grid (capped) / settings (own ScrollViewer removed), then the
  activity log (moved from the footer). Footer: status strip on `IsBusy`
  (indeterminate) / "Level: {ActiveLevelDisplay}" (replaces the level
  status line and "Working…" text) + Create / Close. ViewModel gains
  `Progress`; `Version` = "V006". Ribbon pulldown: V004, V005, V006 (V003
  removed from the ribbon; folder kept).
- SharedStyles: `BrushStageCompleteBackground`, `BrushSheetNoFitBackground`
  (from SmartViewToSheetPlacer V223's inline tints).
- SmartViewToSheetPlacer V224 on the shell (folder
  `SmartViewToSheetPlacer_V224/SmartViewToSheetPlacer_V224`). Header:
  pinned placement metrics. Body: accordion stages 1–5 (state
  unchanged; the body ScrollViewer is now the shell's), then the
  placement-progress card (its own bar dropped) and the activity log
  (both moved from the footer). Every grid's fixed MaxHeight → ratio cap.
  Footer: status strip bound to existing `IsBusy` / `ProgressPercent` /
  `ProgressDetailText` + Export Logs / Close. No ViewModel change. Ribbon
  pulldown: V222, V223, V224 (V221 removed from the ribbon; folder kept).
- AutomatedSectionPlacer V003 on the shell (folder
  `AutomatedSectionPlacer_V003/AutomatedSectionPlacer_V003`) — same
  mapping as SmartViewToSheetPlacer V224: header pinned metrics; body
  accordion stages, progress card (own bar dropped) and activity log
  (moved from the footer); grids ratio-capped; footer status strip on
  `IsBusy` / `ProgressPercent` / `ProgressDetailText` + Export Logs /
  Close. No ViewModel change. Ribbon pulldown: V002, V003 (V001 removed
  from the ribbon; folder kept).
- SharedStyles: `BrushBannerInfo{Background,Border}`,
  `BrushBannerNote{Background,Border,Text}` (from SheetAutoRearrange
  V027's banners).
- SheetAutoRearrange V028 on the shell. Header: active-sheet chip (moved
  from the title bar, now a `BadgePill`) + metric cards. Body: views grid
  (capped) / settings expanders (ViewModel-bound state unchanged; column
  ScrollViewer removed; priority-groups list capped), then the activity
  log and saved-log path (moved from the footer). Footer: status strip on
  `IsBusy` (indeterminate) + Export Log, Run / Close. ViewModel gains
  `Progress` / `SummaryText` (not yet set). Ribbon pulldown: V026, V027,
  V028, V025 (V024 removed from the ribbon; folder kept).
- Named colours (per Rafi, 2026-10-04): every `White` in the migrated
  windows now uses SharedStyles — text/fills on accent backgrounds →
  new `BrushTextOnAccent` (#FFFFFF), white panel backgrounds →
  `BrushCardBackground` (#FFFFFF). 34 uses in 11 windows; no visual
  change. `Transparent` left as is.

## 2026-10-04 — All UI-standard versions: logs collapsed, red Run button

- Every UI-standard tool version from today (Roof, View, Manage, Sheet,
  Setup, Detail Lines, Floor, Dimensions) edited in place — no new version
  numbers:
  - Log panels start **collapsed**. The log header (title + Copy All /
    Copy Selected) stays visible; clicking the title (▸ / ▾) shows or hides
    the log lines. ParaManager V004 and WorksetManager V013 already had a
    collapsed-by-default log toggle and are unchanged.
  - Each tool's main footer action (Run, Create, Place, Generate, Apply…)
    now uses the new red `RunButton` style. Wizard navigation (Next / Back)
    and popover buttons keep the navy PrimaryButton. Schedule Export/Import
    keeps its colour-coded Export (green) / Import (red) buttons.
- `Shared/SharedStyles.xaml`: new `RunButton` (red, same shape as
  PrimaryButton), `LogHeaderToggle` (chevron toggle for log headers) and
  `LogToggleVisibility` converter.

## 2026-10-04 — Dimensions

- Dimensions: DtlLineDim bumped to V009 as a layout-only rebuild per
  `Revit_Plugin_UI_Standard.md` (fixed title bar + version + metrics card,
  scrolling middle, fixed footer with an always-visible log + Copy All /
  Copy Selected, Export Log on the left, Generate Dimensions → Close).
  No behaviour changes.
- Ribbon: the Detail Lines dimension button is now a pulldown — V008 first,
  V009 second.

## 2026-10-04 — Floor Tools

- Floor Tools: both live tools bumped one version as a layout-only rebuild
  per `Revit_Plugin_UI_Standard.md` (fixed title bar + version + metrics
  card, scrolling middle, fixed footer with progress, summaries, an
  always-visible log + Copy All / Copy Selected, Create Floors / Create
  Roof → Close). No behaviour changes.
  New versions: FloorsAndRoofFromLinkedRooms V012,
  FloorsAndRoofFromLinkedRoomsViaPlanView V005 (now a fixed 900 × 760 window
  with a full-height room list; was 420 wide, SizeToContent).
- Ribbon: the From Rooms pulldown lists each approach's previous version
  first and its UI-standard version second.

## 2026-10-04 — Detail Lines

- Detail Lines: every live tool bumped one version as a layout-only rebuild
  per `Revit_Plugin_UI_Standard.md` (fixed title bar + version + metrics
  card, scrolling middle, fixed footer with an always-visible log + Copy All
  / Copy Selected, Primary → Close). No behaviour changes.
  New versions: LinkedDetailLineGenerator VA008 (log Copy buttons now wired;
  metadata stamp `GeneratorVersion` = VA008), DetailLineClosedLoop V002
  (window widened to 980 for a full-height Created Lines grid).
- Ribbon: every Detail Lines button is now a pulldown — current version
  first, the UI-standard version second.

## 2026-10-04 — Setup

- Setup: every live tool bumped one version as a layout-only rebuild per
  `Revit_Plugin_UI_Standard.md` (fixed title bar + version + metrics card,
  scrolling middle, fixed footer with an always-visible log + Copy All /
  Copy Selected, Primary → Close). No behaviour changes.
  New versions: BatchDwgFamilyLinker V002 (first versioned build; now on
  SharedStyles, namespace `BatchDwgFamilyLinker.V002`), DwgToLines V006,
  DwgToDetailLines V012 (window widened to 980 for a full-height layer grid).
- Ribbon: every Setup button is now a pulldown — current version first,
  the UI-standard version second.

## 2026-10-04 — Sheet Tools

- Sheet Tools: every live tool bumped one version as a layout-only rebuild per
  `Revit_Plugin_UI_Standard.md` (fixed title bar + version + metrics card,
  scrolling middle, fixed footer with an always-visible log + Copy All /
  Copy Selected, Primary → Close). No behaviour changes.
  New versions: PlanFromScopeBox V005, SmartViewToSheetPlacer V223,
  AutomatedSectionPlacer V002, SheetAutoRearrange V027.
- Smart Placer / Auto Section Placer: the per-stage Activity Logs (stages 4
  and 5), Export Logs and Cancel buttons moved into one fixed footer; stage
  navigation (Next / Back / Place / Open) stays in the accordion stages.
- Ribbon: every Sheet button is now a pulldown — current version first,
  the UI-standard version second (Auto Section Placer became a pulldown).

## 2026-10-04 — Manage

- Manage: every live tool bumped one version as a layout-only rebuild per
  `Revit_Plugin_UI_Standard.md` (fixed title bar + version + metrics card,
  scrolling middle, fixed footer with log + Copy All / Copy Selected where
  the tool has a log, Primary → Close). No behaviour changes.
  New versions: AnnotationOverlapDetection V003 (now on SharedStyles),
  ParaManager V004, WorksetsElementsBrowser WSEB003, WorksetManager V013,
  WorksetRenamer V004 / FX04, ScheduleExportImport V006.
- Ribbon: every Manage button is now a pulldown — current version first,
  the rebuild second.

## 2026-10-04 — View Tools

- View Tools: every live tool bumped one version as a layout-only rebuild
  per `Revit_Plugin_UI_Standard.md` — three-zone window (fixed title bar +
  version + metrics card, scrolling middle, fixed footer with an
  always-visible log + Copy All / Copy Selected and Primary → Close).
  No behaviour changes; every binding/command is the previous version's.
  New versions: CreateSections V012, RoofEdgeAroundSections V006,
  RoofEdgeElementSections V003, RoofViewFocus V003, APUS V323,
  CalloutCOP V020, RefSectionHeadPlacer V014, SectionViewAutoTagger V005,
  BubbleAutoRenumber V007, SectionAutoRenamer V025, ViewAutoRenamer V005.
- Ribbon: every View Tools button is now a pulldown — current version
  first, the rebuild second.
- Shared: `Shared/Services/LogClipboardService.cs` (same file as the Roof
  Tools PR) for Copy All / Copy Selected from window code-behind.

## 2026-10-04

- Roof Tools (incl. Roof Tag): every live tool bumped one version as a
  layout-only rebuild per `Revit_Plugin_UI_Standard.md` — three-zone
  window (fixed title bar + version + metrics card, scrolling middle,
  fixed footer with an always-visible log + Copy All / Copy Selected and
  Primary → Close buttons). No behaviour changes; every binding/command is
  the previous version's. New versions: AutoSlopeByPoint V029, Ridge V002,
  MultiCopies29, AutoSlopeByDrain V011, InnerLoopDivider V010,
  OuterCurveDivider V005, RoofDetailLineIntersect V013,
  InnerLoopsAndPerpendicular V006, VertexReducer V008, MultiplePoints V002,
  RoofRidgeLines V069 / V058, CreaserAdv V011, RoofTag V017,
  RoofFromDetailLines V008, CombinedRoofTools V002, RoofTypeCreator V002,
  RoofPointComparison V002, RoofPointElevationSync V004.
- Ribbon: every Roof Tools button is now a pulldown — current version
  first, the rebuild second. Existing multi-version pulldowns get each
  rebuild right after the version it was copied from.
- Shared: added `Shared/Services/LogClipboardService.cs` (Copy All / Copy
  Selected for any log ListBox, used from window code-behind).

## 2026-09-20

- Added baseline documentation set: `CLAUDE.md`, `Claude/ARCHITECTURE.md`,
  `Claude/RIBBON_AND_ICONS.md`, `Claude/TOOL_VERSIONING.md`.
- Ribbon: every button now shows a version tooltip; buttons with no
  surviving source were dropped.
- Added Edge Element pulldown with `RoofEdgeElementSections` V002.

## 2026-09-19

- Fixed a ribbon startup failure: V005 icon wasn't embedded, and dead V003
  buttons were still wired — dropped them.
- Fixed `AutoSlopeByDrain` V010 `Parameter 'source'` init failure.
- Ribbon: consolidated to one icon per tool, with versions listed as
  individual push buttons inside a dropdown (see `Claude/RIBBON_AND_ICONS.md`
  for the current pulldown-wiring pattern this led to).
- Added `ParaManager` V003 — 4-step wizard UX.
- Standardized run timing/logging across the `ByPoints` AutoSlope engines to
  match `ByDrains`.

## 2026-09-18

- `AutoSlopeByDrain` bumped to V010 — multi-roof support (see the "per
  Rafi's confirmed multi-roof decision (2026-09-08)" note in
  `Commands/AutoSlopeDrainCommand.cs`, and the `TransactionGroup` pattern
  this introduced, documented in `Claude/ARCHITECTURE.md` §5).
- Added `RoofEdgeAroundSections` V005 with V011-style section orientation.
- Callout COP window now displays its version as `V19.0`; its view grid was
  advanced to a sortable/filterable/searchable datagrid.
- In-progress updates across RoofTools, Sheet, FloorTools, and Setup tools.

## 2026-09-17

- `ParaManager` bumped to V002, adopting the shared `Settings`/`Log`
  services from `Shared/` instead of a local implementation.
- Root-tag brushes in `ParaManager` and `WorksetRenamer` switched to
  `DynamicResource` (from `StaticResource`) — a pack-URI/resource-lookup fix.
- Run start/end/duration now logged in all AutoSlope roof-slope tools.
- `AutoSlopeByDrain` V009: drain grid now shows circle diameter/radius.
- Drain tolerance matching restricted to the same roof edge/opening
  (cross-edge false matches fixed).
- Missing ribbon icons assigned to "Worksets From Links" and
  "By Drain (Multi) V009".

## 2026-09-16

- `AutoSlopeByDrain` bumped to V009.
- Added Tool 007, a boundary-scoped detail line generator.
- `ParaManager` added to the Manage ribbon panel; startup/build bugs fixed.
- Fixed an `AutoSlope_HighestElevation` type mismatch in `ByDrain` tools.
- AutoSlope shared parameters standardized across all RoofSlope tools —
  a cross-tool convention change (parameter names/types now consistent
  between `ByPoints` and `ByDrains` engines).

## 2026-09-15

- Added `SheetAutoRearrange` V026 with configurable priority groups.
- Ribbon: split buttons replaced with pulldown buttons for every
  multi-version tool (an earlier ribbon-layout iteration than the
  2026-09-19 one-icon-per-tool consolidation above) — fixed blank text and
  broken rendering on stacked buttons along the way.
  RibbonLayoutHelper's stacked-item text-rebinding workaround (see
  `Claude/RIBBON_AND_ICONS.md`) traces back to this pass.
  Combined Roof Tools grouped with the two Auto Slope By Point variants;
  single-tool roof panels merged; empty `QuickAcces` panel dropped.
- Ribbon icons renamed to encode their real size in the filename (the
  `_16`/`_32` suffix convention documented in `Claude/RIBBON_AND_ICONS.md`
  dates from here); all 51 icons shrunk to 16×16 with 32×32 kept as backups.
- Ribbon tab name and build output path bumped (recurring machine-specific
  `BaseOutputPath` bumps — see the `HintPath`/build-prerequisite note in
  `CLAUDE.md`).

## 2026-09-14

- Ribbon panels split by section, version numbers dropped from button
  labels, then flattened to plain icon buttons with no dropdown menus (an
  earlier layout tried and later revised — see 2026-09-15/09-19 above).
- Added Auto Slope V028 Excel export: run start/end time and total seconds.
- Slope Percentage made editable in AutoSlope V028; the HTML preview
  experiment for its window was dropped after being tried.

## 2026-09-13

- Added `AutoSlopeByPointRidge` V001 (V028 plus ridge-point handling) —
  the ridge-aware variant that later became one of the three coexisting
  "By Point" implementations described in `Claude/TOOL_VERSIONING.md`.

## 2026-09-11

- Superseded tool versions removed; all remaining active tool folders
  backed up.
- Combined Roof Tools gained a single "Run All" button, defaulting to the
  Auto Slope tab.
- Setup Tools converted from SplitButton to PulldownButton.
- Every ribbon button given its own icon (the per-button-icon convention
  in `Claude/RIBBON_AND_ICONS.md` dates from here — before this, only
  pulldown headers had icons).
- Added an optional max-slope filter to Multiple Points; fixed its edge
  extraction on sloped/shaped roofs.

## 2026-09-10

- Added Multiple Points roof tool (midpoint, quarter points, extra points
  on long edges).
- Added Combined Roof Tools window, merging 5 roof tools into one tabbed UI.
- Setup folder nesting flattened; added DWG To Detail Lines V002; dropped
  dead ribbon buttons.
- Setup tools split into Family/Project ribbon groups; added VA006 detail
  line tool.

## 2026-09-09

- Plugin modules reorganized into the `Core/Commands/UI` structure that
  `CLAUDE.md` and `Claude/ARCHITECTURE.md` now document as the standing
  convention.
