# Wet Area Waterproofing — Part A Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Wet Area Waterproofing V001 Part A: find and filter wet rooms (host + links), repair room outlines, create waterproofing roofs on the slab under each room, and write an Excel report; plus "Remove a run".

**Architecture:** Revit-free core (`Core/Geometry`, `Core/Grid`, `Core/Matching`, `Core/Report`, `Core/Models`) carries all logic that can be unit-tested; `Infrastructure/` adapts Revit (collect rooms/slabs, build loops, create roofs) behind an `IWetAreaSession` driven by an ExternalEvent, the same pattern as `Menu/Sheet/SectionsToSheet_V001`. UI is a modeless WPF window on the shared `ToolWindowShell`.

**Tech Stack:** .NET 8, WPF, Revit 2026 API (`Autodesk.Revit.SDK`), CommunityToolkit.Mvvm, ClosedXML 0.105, Clipper2 1.4, xUnit (tests link tool files, like `Tests/SectionsToSheet.Tests`).

**Spec:** `docs/superpowers/specs/2026-11-14-wet-area-waterproofing-design.md` · Preview `Claude/Previews/WetAreaWaterproofing_V001_Window.html` (https://claude.ai/artifact/AxccNteVMVibgsyeXAjEiq)

## Global Constraints

- Folder `Menu/Sheet/WetAreaWaterproofing_V001/{Commands,Core,UI,Infrastructure}`; namespace root `Revit26_Plugin_Revised.WetAreaWaterproofing.V001`.
- View model `ViewModel_WetAreaWaterproofing` (partials `ViewModel_WetAreaWaterproofing.<Part>.cs`).
- `ToolCatalog.WetAreaWaterproofing = Add("Wet Area Waterproofing", "1.0.0", "V001", "Revit26_Plugin_Revised.WetAreaWaterproofing.V001")`.
- Plugin `<Version>` → **7.11.0** (CHANGELOG is at 7.10.1 while `.csproj` says 7.8.0; align both to 7.11.0).
- Every `IExternalCommand.Execute` / `IExternalEventHandler.Execute` delegates to `ToolGuard.RunCommand` / `ToolGuard.RunHandler`.
- Model edits only in `using Transaction` inside one `TransactionGroup` per run, `Assimilate()` on success = one undo step; roll back a room's transaction on failure and continue.
- Settings: `SettingsService<WetAreaSettings>.Load("WetAreaWaterproofing")`, file has `Version`.
- Units in core: millimetres and m²; feet only in `Infrastructure/`.
- `///` comments on every public type/member. Build `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` → 0 errors; `dotnet test Tests/WetAreaWaterproofing.Tests` → green.
- Comments tag format (exact): `WP|<RunId>|<RoomKey>` where RunId = `yyyyMMdd-HHmmss`, RoomKey = `<SourceKey>:<RoomUniqueId>`; SourceKey = `Host` or the link instance UniqueId.
- Defaults (exact): keywords `toilet, wc, bath, shower, kitchen, pantry, laundry, wash, balcony`; snap 5 mm; bridge 50 mm; area tolerance 2 %; approximate off; offset 0 mm; auto-save report on; report folder `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\Reports`; file `WP_Report_<Model>_<yyyyMMdd_HHmm>.xlsx`.

## Review Focus

1. **Linked room with link transform rotated/moved** → outline and slab search must be in host coordinates (test: `LoopTransformTests.RotatedLinkLoopIsInHostCoords` in Task 6 via pure `Transform2D`).
2. **Room with an inner loop (column) that fails repair** → outer loop kept, hole dropped, issue logged (`RepairChainTests.FailingInnerLoopIsDroppedNotFatal`, Task 3).
3. **Re-run on rooms already done** → old roof for that RoomKey replaced, not duplicated (`RunPlannerTests.ExistingRoofIsReplaced`, Task 5).
4. **Keyword partial matches** ("Washroom" yes, "Dishwasher store" also matches `wash`) → match whole-word-prefix only: `wash` matches "Washroom", not "Dishwasher" (`KeywordMatcherTests`, Task 2).
5. **Report folder missing / file open in Excel** → create folder; on IOException write `_1` suffix and log, never throw (`ReportWriterTests.LockedFileGetsSuffix`, Task 7).

---

### Task 1: Tool skeleton, catalog, ribbon, settings

**Files:**
- Create: `Menu/Sheet/WetAreaWaterproofing_V001/Commands/WetAreaWaterproofingCommand.cs`
- Create: `Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/WetAreaHandler.cs`
- Create: `Menu/Sheet/WetAreaWaterproofing_V001/Core/Models/WetAreaSettings.cs`, `Core/Models/Enums.cs`
- Create: `Resources/Icons/SheetTools/WetAreaWaterproofing_16.png`, `_32.png` (draw with PIL: navy floor plate + blue droplet; embed like the other icons)
- Modify: `Shared/Services/ToolCatalog.cs` (entry), `Revit26_Plugin_Revised.csproj` (Version 7.11.0), `Menu/00_Push_Button_Menu_items/Ribbon/SheetToolsRibbon.cs` (push button after Combine Sheets in "Sheet Create")
- Modify: `README.md` Ribbon section, `Claude/Revit26_Plugin_Md Files/Claude/RIBBON_AND_ICONS.md` (Sheet Create row + icon note)
- Create: `Tests/WetAreaWaterproofing.Tests/WetAreaWaterproofing.Tests.csproj` (links `Core/**/*.cs`; packages xunit as in SectionsToSheet.Tests plus `Clipper2 1.4.0`, `ClosedXML 0.105.0`), `SettingsTests.cs`

**Interfaces:**
- Produces: `WetAreaSettings` { `int Version=1`, `List<string> Keywords`, `double SnapMm=5`, `double BridgeMm=50`, `double AreaTolerancePct=2`, `bool AllowApproximate=false`, `string RoofTypeName`, `double OffsetMm=0`, `string WorksetName`, `bool IncludeHost=true`, `List<string> IncludedLinks`, `bool AutoSaveReport=true`, `string ReportFolder`, `bool WriteComments=true`, `ReRunMode ReRun=ReRunMode.UpdateInPlace`, `GridState Grid` } and `WetAreaSettings Normalize()`.
- Produces: `enum ReRunMode { UpdateInPlace }`, `enum NoSlabMode { LevelPlusOffset }`, `enum RoomStatus { Ready, Repaired, AreaWarning, Approximate, NoSlab, Owned, NotEnclosed, AlreadyDone, Failed }`.
- Ribbon: `new PushButtonData("Btn_WetAreaWaterproofing_V001", "Wet Area Waterproofing", assemblyPath, "Revit26_Plugin_Revised.WetAreaWaterproofing.V001.Commands.WetAreaWaterproofingCommand")`, tooltip `ToolCatalog.WetAreaWaterproofing.Tip("Finds wet rooms, repairs their outlines and adds a waterproofing roof on top of each slab. Writes an Excel report.")`.

- [ ] **Step 1: Write failing tests** `SettingsTests.DefaultsMatchSpec` (asserts every default listed in Global Constraints) and `SettingsTests.NormalizeClampsAndFillsNulls` (`SnapMm=-1 → 5`, `BridgeMm < SnapMm → SnapMm`, `AreaTolerancePct=0 → 2`, null `Keywords` → defaults, empty `ReportFolder` → default path).
- [ ] **Step 2: Run** `dotnet test Tests/WetAreaWaterproofing.Tests` → FAIL (types missing).
- [ ] **Step 3: Implement settings + enums**; command mirrors `SectionsToSheetCommand` (project/family/read-only checks, load settings, create session + window, modeless `Show`); handler mirrors `SectionsToSheetHandler` with `ToolCancellation.BeginFor(ToolCatalog.WetAreaWaterproofing, app)`. Window is a placeholder until Task 8 (command shows a TaskDialog "coming in this build" only if Task 8 is not done — remove in Task 8).
- [ ] **Step 4: Run tests** → PASS; **build** → 0 errors.
- [ ] **Step 5: Commit** `feat(wet-area): skeleton, catalog 1.0.0, ribbon button, settings`.

### Task 2: Wet-room keyword matcher

**Files:** Create `Core/Matching/KeywordMatcher.cs`; Test `Tests/WetAreaWaterproofing.Tests/KeywordMatcherTests.cs`

**Interfaces:**
- Produces: `static string KeywordMatcher.Match(string roomName, IEnumerable<string> keywords)` → first matching keyword or null.

- [ ] **Step 1: Failing tests** — `("Toilet 01",kw)→"toilet"`, `("Master Bath")→"bath"`, `("WASHROOM")→"wash"`, `("Dishwasher Store")→null`, `("W.C.")→"wc"` (punctuation ignored), `("Bedroom")→null`, `(null)→null`, custom keyword list honoured.
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement**: normalise (lower-case, strip `.`/`-`/`_` to spaces, collapse), split into words, match when a word **starts with** the keyword.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(wet-area): keyword matcher`.

### Task 3: Outline repair chain (pure geometry)

**Files:**
- Create: `Core/Geometry/Pt.cs` (record struct `Pt(double X, double Y)` in mm), `Core/Geometry/Polyline2D.cs` (area, signed area, perimeter, `IsClosed`, `SelfIntersects`), `Core/Geometry/LoopRepair.cs`, `Core/Geometry/RepairStep.cs`, `Core/Geometry/Hull.cs`
- Test: `Tests/WetAreaWaterproofing.Tests/LoopRepairTests.cs`, `RepairChainTests.cs`

**Interfaces:**
- Consumes: `WetAreaSettings` (SnapMm, BridgeMm, AreaTolerancePct, AllowApproximate).
- Produces:
  - `record Seg(Pt A, Pt B)` (arcs arrive already tessellated by Task 6).
  - `record RepairStep(int Method, string Name, bool Passed, string Detail, Pt? At)`.
  - `record LoopResult(IReadOnlyList<Pt> Points, bool Ok, IReadOnlyList<RepairStep> Steps, bool Approximate)`.
  - `static LoopResult LoopRepair.Repair(IReadOnlyList<Seg> segs, WetAreaSettings s)` — methods 2–7 in order: Clean(2) → MergeCollinear(3) → Snap(4) → Bridge(5) → RemoveOverlaps(6, Clipper2 `Clipper.Union` on the polygon, keep largest) ; returns first state that `IsClosed && !SelfIntersects`. Every method tried appends a `RepairStep`, passed or not.
  - `record RoomOutline(LoopResult Outer, IReadOnlyList<LoopResult> Inner, double AreaM2, double RoomAreaM2, double AreaDiffPct, RoomStatus Status, IReadOnlyList<RepairStep> Steps)`.
  - `static RoomOutline LoopRepair.Evaluate(IReadOnlyList<IReadOnlyList<Seg>> loops, double roomAreaM2, WetAreaSettings s, Func<IReadOnlyList<Pt>> bottomFace3D)` — outer = loops[0]; if outer fails try `bottomFace3D` (method 8); if still failing and `AllowApproximate` use `Hull.Convex` (method 10, Status Approximate); failing inner loops dropped (method 9, logged). Status: Ready (no repair) / Repaired / AreaWarning (|diff| > tol, roof still created) / Approximate / Failed.

- [ ] **Step 1: Failing tests** (all mm):
  - `CleanSquareNeedsNoRepair`: 4000×3000 rectangle → Ok, Status Ready, AreaM2 12.0.
  - `ShortSegmentsRemoved`: rectangle with a 0.5 mm stub → Ok, step 2 passed.
  - `ReversedAndShuffledSegmentsAreChained`.
  - `CollinearPiecesMerged`: 4 m edge in 8 pieces → 4 points.
  - `GapOf3mmIsSnapped`: Ok, Status Repaired, step 4 detail contains "3 mm".
  - `GapOf30mmIsBridged`: Ok via method 5.
  - `GapOf80mmFailsWithoutApproximate`: Ok=false, Status Failed.
  - `GapOf80mmApproximateWhenAllowed`: Status Approximate.
  - `BowTieOverlapRemoved`: self-crossing loop → Ok via method 6.
  - `AreaOutsideToleranceIsWarningNotFailure`: outline 3.4 % larger than room → Status AreaWarning, AreaDiffPct ≈ 3.4.
  - `BottomFaceUsedWhenOuterFails`: delegate returns a clean square → Ok, step 8 passed.
  - `FailingInnerLoopIsDroppedNotFatal` (Review Focus 2).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** per Interfaces. Snap: pair each open end with the nearest other open end within SnapMm; if both adjoining segments are lines and not parallel, move both ends to the lines' intersection when it is within SnapMm of each, else to their midpoint. Bridge: add a segment between nearest open ends ≤ BridgeMm. Clipper2 works in `long` — scale mm ×1000.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(wet-area): outline repair chain with tests`.

### Task 4: Room grid filtering, sorting, level buttons

**Files:** Create `Core/Grid/RoomRow.cs`, `Core/Grid/GridState.cs`, `Core/Grid/GridQuery.cs`; Test `GridQueryTests.cs`

**Interfaces:**
- Produces:
  - `RoomRow` (plain data, observable bits live in the UI wrapper): `string RoomKey, string Source, string Level, double LevelElevationMm, string Number, string Name, double AreaM2, string Matched, string SlabText, double? SlabTopMm, RoomStatus Status, string StatusDetail, string Owner`.
  - `GridState`: `HashSet<string> Levels` (empty = all), `Dictionary<string,HashSet<string>> TickFilters` (columns Source/Level/Matched/Status), `Dictionary<string,string> Contains` (Number/Name), `double? AreaMin, AreaMax`, `string Search`, `List<(string Column,bool Desc)> Sort`, `string GroupBy` (None/Level/Source/Matched), `bool WetOnly=true`, `List<string> HiddenColumns`.
  - `static IReadOnlyList<RoomRow> GridQuery.Apply(IEnumerable<RoomRow> rows, GridState st)`.
  - `static IReadOnlyList<(string Level,int Count)> GridQuery.LevelCounts(IEnumerable<RoomRow> rows, bool wetOnly)` ordered by elevation.
  - `static void GridState.ToggleLevel(string level, bool solo)` (Ctrl+click = solo; clicking the only active level again = all).
  - `static int GridQuery.ActiveFilterCount(GridState st)`.
- [ ] **Step 1: Failing tests**: level filter (L01+L02 of L01/L02/L03), ToggleLevel solo/all, tick filter on Source, contains on Name (case-insensitive), area range inclusive, global search across Number/Name/Source/Matched/Level, multi-sort (Level asc then Number desc), WetOnly hides `Matched==null`, ActiveFilterCount, LevelCounts ordered by elevation not name ("L10" after "L2").
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(wet-area): grid query engine`.

### Task 5: Run planner (re-run, ownership, slab rules)

**Files:** Create `Core/Models/RoomCandidate.cs`, `Core/Run/RunPlanner.cs`, `Core/Run/RunTag.cs`; Test `RunPlannerTests.cs`, `RunTagTests.cs`

**Interfaces:**
- Produces:
  - `RunTag`: `static string Make(string runId, string roomKey)` → `WP|<runId>|<roomKey>`; `static bool TryParse(string comments, out string runId, out string roomKey)`; `static string NewRunId(DateTime now)` → `yyyyMMdd-HHmmss`.
  - `RoomCandidate`: `RoomRow Row`, `RoomOutline Outline`, `double LevelElevationMm`, `double? SlabTopMm`, `IReadOnlyList<long> ExistingRoofIds`, `string Owner` (null = free).
  - `record RoofJob(string RoomKey, IReadOnlyList<Pt> Outer, IReadOnlyList<IReadOnlyList<Pt>> Inner, double BaseOffsetFromLevelMm, IReadOnlyList<long> ReplaceRoofIds, string CommentsTag)`.
  - `record PlannedRun(string RunId, IReadOnlyList<RoofJob> Jobs, IReadOnlyList<Issue> Issues)`; `record Issue(string Severity, string Step, string RoomKey, string Number, string Message, string Fix)`.
  - `static PlannedRun RunPlanner.Plan(IEnumerable<RoomCandidate> ticked, WetAreaSettings s, string runId)`.
- Rules: Owner != null → skip, Issue "Owned by <user>"; Outline Failed/NotEnclosed → skip, Issue; SlabTopMm null → offset = `OffsetMm`, Issue warning "No slab: level + N mm"; else offset = `SlabTopMm - LevelElevationMm + OffsetMm`; AreaWarning/Repaired/Approximate → job + Issue warning with AreaDiffPct; ExistingRoofIds → `ReplaceRoofIds` (UpdateInPlace).
- [ ] **Step 1: Failing tests**: `RunTag` round-trip and garbage → false; `ExistingRoofIsReplaced` (Review Focus 3); `OwnedRoomSkippedWithIssue`; `NoSlabUsesLevelPlusOffset`; `SlabOffsetComputed` (slab 3150, level 3000, offset 0 → 150); `AreaWarningStillCreatesJob`.
- [ ] **Step 2–4:** fail → implement → pass.
- [ ] **Step 5: Commit** `feat(wet-area): run planner`.

### Task 6: Revit reading (rooms, links, slabs, loops, worksharing)

**Files:** Create `Infrastructure/RoomCollector.cs`, `Infrastructure/SlabFinder.cs`, `Infrastructure/BoundaryReader.cs`, `Infrastructure/TargetSourceReader.cs` (models + link list for the "Search in" tree; categories used in Part B), `Core/Geometry/Transform2D.cs`; Test `LoopTransformTests.cs` (pure part only)

**Interfaces:**
- Consumes: `KeywordMatcher.Match`, `LoopRepair.Evaluate`, `RunTag.TryParse`.
- Produces:
  - `record Transform2D(double Ox, double Oy, double AngleRad)` with `Pt Apply(Pt p)`; built from a link's `Transform` (origin + BasisX).
  - `static IReadOnlyList<RoomCandidate> RoomCollector.Collect(Document doc, WetAreaSettings s, Action<string> log)`:
    - host rooms (`SpatialElement` Room, `Area > 0`, `Location != null`) when `IncludeHost`; each loaded `RevitLinkInstance` whose name is in `IncludedLinks` (empty list = all loaded).
    - Level: host level by name; linked room → host level with nearest elevation (≤ 1 mm) else link level name, Source = link instance name.
    - Loops: `BoundaryReader.Read(room, transform, s)` tries `SpatialElementBoundaryLocation` Finish → Center → CoreBoundary → CoreCenter (method 1), tessellates arcs at 50 mm chord, converts ft→mm and applies `Transform2D`, then `LoopRepair.Evaluate(..., bottomFace3D: () => bottom face of SpatialElementGeometryCalculator solid)`.
    - Slab: `SlabFinder.FindTopMm(doc, link, outline, levelElevMm)` — floors (`OST_Floors`) in host first, then same link; candidate if its bounding box contains the outline centroid in XY and its top face elevation ∈ [level − 500 mm, level + 500 mm]; pick highest top; top from `HOST_ELEVATION ... ` via `BoundingBoxXYZ.Max.Z` refined by the top face (`HostObjectUtils.GetTopFaces`) when available.
    - Owner: `WorksharingUtils.GetCheckoutStatus` on the room (host only) and slab; `OwnedByOtherUser` → `Owner = GetWorksharingTooltipInfo(...).Owner`.
    - ExistingRoofIds: host `OST_Roofs` whose Comments parse via `RunTag` with the same RoomKey.
- [ ] **Step 1: Failing test** `RotatedLinkLoopIsInHostCoords` (link origin (10000,5000), 90° → (1000,0) maps to (10000,6000)).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** `Transform2D` and the Revit readers. **Step 4: Run tests** → PASS; **build** → 0 errors.
- [ ] **Step 5: Commit** `feat(wet-area): read rooms, links, slabs and boundaries`.

### Task 7: Excel report

**Files:** Create `Core/Report/RunReport.cs` (data), `Core/Report/ReportWriter.cs`; Test `ReportWriterTests.cs`

**Interfaces:**
- Consumes: `PlannedRun`, `RoomCandidate`, `RepairStep`, `Issue`.
- Produces:
  - `RunReport`: `Summary` (RunId, DateTime, User, Model, PluginVersion, ToolVersion, settings snapshot, totals, Duration), `List<ReportRoom>` (RoomId, Source, Level, Number, Name, RoomAreaM2, Matched, SlabId, SlabTopMm, RoofId, RoofAreaM2, AreaDiffPct, BoundaryResult, Status, Reason, Phase, Workset, Group, Scale — Group/Scale empty in Part A), `List<ReportRepair>` (RoomId, Number, Name, Method, MethodName, Result, Detail, X, Y, AreaBefore, AreaAfter), `List<Issue>`.
  - `static string ReportWriter.Write(RunReport r, string folder, string modelName)` → full path written.
- Sheets and column order (fixed, append-only later): **Summary**, **Rooms**, **Boundary repair**, **Issues** (Sections and Sheets added in Parts B/C). Header row: bold, AutoFilter, frozen; status fill green `#E6F4EC` (Ready), yellow `#FDF1E1` (Repaired/AreaWarning/Approximate/NoSlab), red `#FBE8E7` (Failed/Owned/NotEnclosed). ElementIds written as numbers.
- [ ] **Step 1: Failing tests** (write to a temp folder, reopen with ClosedXML): sheet names and order; Rooms header row exactly as listed; a skipped room row present with Reason; status fill colours; file name pattern `WP_Report_<Model>_<yyyyMMdd_HHmm>.xlsx`; `MissingFolderIsCreated`; `LockedFileGetsSuffix` (Review Focus 5: hold a `FileStream` open with `FileShare.None` → `_1` file written).
- [ ] **Step 2–4:** fail → implement → pass.
- [ ] **Step 5: Commit** `feat(wet-area): Excel run report`.

### Task 8: Roof creation, session, Remove a run

**Files:** Create `Infrastructure/IWetAreaSession.cs`, `Infrastructure/RevitWetAreaSession.cs`, `Infrastructure/RoofCreator.cs`, `Infrastructure/RunRemover.cs`, `Infrastructure/RoofFailuresPreprocessor.cs`

**Interfaces:**
- Consumes: `RoomCollector.Collect`, `RunPlanner.Plan`, `ReportWriter.Write`, `RunTag`.
- Produces `IWetAreaSession`:
  - `IReadOnlyList<RoomCandidate> Initial`, `IReadOnlyList<NamedType> RoofTypes`, `IReadOnlyList<string> Worksets` (empty if not workshared), `IReadOnlyList<string> Links`.
  - `bool Refresh(WetAreaSettings s, Action<IReadOnlyList<RoomCandidate>> done)`.
  - `bool Run(IReadOnlyList<string> roomKeys, WetAreaSettings s, Action<RunOutcome> done)`; `record RunOutcome(string RunId, int Created, int Replaced, int Skipped, IReadOnlyList<Issue> Issues, string ReportPath, bool Cancelled)`.
  - `bool ListRuns(Action<IReadOnlyList<(string RunId,int Count)>> done)`; `bool RemoveRun(string runId, Action<(int Deleted,int SkippedOwned)> done)`.
- RoofCreator: per job one `Transaction` named `WP roof <Number>`; delete `ReplaceRoofIds`; `doc.Create.NewFootPrintRoof(CurveArray outer (+inner loops), level, roofType, out _)`; set `ROOF_LEVEL_OFFSET_PARAM` = offset (mm→ft); Comments = `CommentsTag` when `WriteComments`; workset via `ELEM_PARTITION_PARAM` = picked workset id when workshared; failures via `RoofFailuresPreprocessor` (delete warnings, error → rollback that room, Issue). All jobs inside one `TransactionGroup("Wet Area Waterproofing")` → `Assimilate()`. Cancellation via `ToolCancellation` rolls back the group.
- RunRemover: finds host elements whose Comments parse to the RunId; skips ones owned by others; deletes in one transaction.
- [ ] **Step 1:** Implement (Revit-bound; no unit test) following `RevitSectionsSession` structure.
- [ ] **Step 2: Build** → 0 errors.
- [ ] **Step 3: Commit** `feat(wet-area): roof creation, session, remove run`.

### Task 9: Window and view model

**Files:** Create `UI/Views/WetAreaWaterproofingWindow.xaml(.cs)`, `UI/ViewModels/ViewModel_WetAreaWaterproofing.cs`, `.Grid.cs`, `.Run.cs`, `UI/ViewModels/RoomRowViewModel.cs`, `UI/ViewModels/LevelButtonViewModel.cs`, `UI/Views/ColumnFilterPopup.xaml(.cs)`

**Interfaces:**
- Consumes: `IWetAreaSession`, `GridQuery`, `GridState`, `WetAreaSettings`.
- Layout (labels exactly as the preview): `ToolWindowShell` Title "Wet Area Waterproofing", version from `ToolCatalog.WetAreaWaterproofing`. Left card **1 · Rooms**: Source (Host model / Links ▾), Wet keywords chips (+ Add), Show (Wet rooms only / All rooms), **Levels** buttons with counts (click toggle, Ctrl+click solo), search box, Group by ▾ (None/Level/Source/Matched), Columns ▾, Clear filters; DataGrid columns ☐, Source, Level, No., Name, Area m², Matched, Slab top, Status (Doors/Drains/Fixtures/Group/Sections columns hidden until Parts B/C); header click sort, Shift+click adds sort; ⏷ opens `ColumnFilterPopup` (tick list / contains / min–max); Tick visible, Untick visible, Invert; status text "`{shown} shown · {ticked} ticked · {total} total · {n} filters active`". Right card **2 · Waterproofing roof**: Roof type ▾, Workset ▾, Offset above slab (mm), No slab found ▾ (Use room level + offset), ☑ Write run ID + room No. + name to Comments, ☐ Allow approximate outline, **Boundary repair**: Snap gaps up to (mm), Bridge gaps up to (mm), Area check ± (%). Report: ☑ Save Excel report after run, Export now, Report folder … . Footer: warning bar "⚠ {n} rooms need attention: …  Show these rooms", Remove a run…, Cancel, **Run for {n} rooms**; result card listing roofs created/replaced, skipped, warnings, report path, Open report / Open folder.
- Grid state, column order/widths and filters saved to `settings.Grid` on close.
- [ ] **Step 1:** Implement window + VM; remove the Task 1 placeholder.
- [ ] **Step 2: Build** → 0 errors; **tests** still green.
- [ ] **Step 3: Commit** `feat(wet-area): window, advanced room grid, run and remove`.

### Task 10: Docs, preview, PR

**Files:** Modify `CHANGELOG.md` (`## [7.11.0] — <date>` Added: Wet Area Waterproofing v1.0.0 Part A, user note), `Menu/Sheet/WetAreaWaterproofing_V001/README.md` (create), `Claude/Previews/WetAreaWaterproofing_V001_Window.html` (header comment: plugin 7.11.0, mirrors the real XAML path; tag Part B/C sections as "Coming in Part B/C"), PR #139 description.

- [ ] **Step 1:** Update docs and preview; republish the Artifact to the same link.
- [ ] **Step 2: Final verification**: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` → 0 errors; `dotnet test Tests/WetAreaWaterproofing.Tests` → all pass.
- [ ] **Step 3: Commit + push**, update PR #139 body (preview link, test list, "needs testing in Revit 2026").
