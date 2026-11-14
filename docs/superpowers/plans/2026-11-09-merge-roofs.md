# Merge Roofs V001 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Select 2+ footprint roofs and create one new footprint roof from their merged outline (outer boundary + holes), keeping the originals.

**Architecture:** A Revit-free `Core` (footprint model, analysis, Clipper2 union, slope mapping, arc re-fit) is unit-tested in `Tests/MergeRoofs.Tests`. Thin `Infrastructure` adapters read roofs, create the roof (arcs first, then segments) and draw detail lines/circles. A modal WPF window drives Check -> Create inside `ToolGuard.RunCommand`.

**Tech Stack:** .NET 8, WPF, Revit 2026 API, Clipper2 (NuGet), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-09-merge-roofs-design.md`

## Global Constraints

- Location `Menu/RoofTools/Modelling/MergeRoofs_V001/{Commands,Core,UI,Infrastructure}`; namespace `Revit26_Plugin_Revised.MergeRoofs.V001`.
- Footprint roofs only; at least 2 roofs; skip roofs that are read-only or checked out by another user (`WorksharingUtils.GetCheckoutStatus`).
- Gap tolerance default 5 mm, user adjustable; units inside Core are Revit internal feet (`mm / 304.8`).
- New roof: type + level of the first selected roof; per-edge slope copied from the source edge; originals untouched.
- Arcs: try once with arcs, retry once with flattened segments; window reports which was used.
- Roof failure -> two separate prompts: profile as detail lines in the current view, then circles at issue areas. Nothing else changes; transaction rolled back.
- Entry point via `ToolGuard.RunCommand`; model changes inside `using` `Transaction`; errors shown in the window or logged via `ToolGuard.Report`.
- Ribbon button internal name `Btn_MergeRoofs_V001`; label "Merge Roofs" (no version); tooltip via `ToolCatalog.MergeRoofs.Tip(...)`.
- Tool version 1.0.0 in `ToolCatalog.cs`; bump plugin `<Version>` (currently 6.39.0) to 6.40.0; CHANGELOG entry; `///` comments on public types.
- Settings (tolerance, checkbox) in `%AppData%\Revit26_Plugin_Revised\MergeRoofs\settings.json` with a `Version` field.
- Build: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` must give 0 errors.

## Review Focus

- Roofs touching at a single point only -> reported "not joined", no roof created.
- Two identical roofs / one roof fully inside another -> union equals the outer one, no crash, no duplicate loops.
- A ring of roofs enclosing a courtyard -> courtyard becomes an interior loop (hole), not an error.
- Gap exactly equal to the tolerance and tolerance 0 -> deterministic (gap <= tolerance snaps; 0 snaps nothing).
- Output segments shorter than Revit's short-curve limit (~0.00256 ft) -> removed before sketch creation.
- Roofs on different levels or types -> first roof wins, window shows a note (no failure).
- Selection with one roof, or a non-footprint roof -> clear message, window does not open.

---

### Task 1: Core footprint model, union and analysis

**Files:**
- Create: `Menu/RoofTools/Modelling/MergeRoofs_V001/Core/Models/{FootprintModels.cs,MergeSettings.cs}`
- Create: `Menu/RoofTools/Modelling/MergeRoofs_V001/Core/Services/{RoofUnion.cs,MergeAnalyzer.cs,SlopeMapper.cs}`
- Create: `Tests/MergeRoofs.Tests/MergeRoofs.Tests.csproj` (links `Core\**\*.cs`; xUnit as in `Tests/RoofPointsAndSlope.Tests`; `Clipper2` 1.4.0)
- Test: `Tests/MergeRoofs.Tests/{UnionTests.cs,AnalyzerTests.cs,SlopeMapperTests.cs}`

**Interfaces:**
- Produces:
  - `readonly record struct Pt(double X, double Y)`
  - `sealed class Edge { Pt A; Pt B; Pt? ArcMid; bool DefinesSlope; double SlopeAngle; }` (ArcMid non-null = arc through A, ArcMid, B)
  - `sealed class Loop { IReadOnlyList<Edge> Edges; }`; `sealed class RoofFootprint { string Id; Loop Outer; IReadOnlyList<Loop> Holes; }`
  - `enum IssueKind { Overlap, Intersection, Gap, NotJoined }`; `sealed record Issue(IssueKind Kind, Pt Location, string Message)`
  - `sealed record GapLink(Pt A, Pt B)` (connecting line across a snapped gap)
  - `sealed class MergeResult { IReadOnlyList<Loop> Outer; IReadOnlyList<Loop> Holes; IReadOnlyList<Issue> Issues; IReadOnlyList<GapLink> GapLinks; bool CanCreate; }` — `CanCreate` is true only when exactly one outer loop exists and no `NotJoined` issue.
  - `static MergeResult MergeAnalyzer.Analyze(IReadOnlyList<RoofFootprint> roofs, double toleranceFeet)`
  - `static IReadOnlyList<Edge> SlopeMapper.Apply(IReadOnlyList<Edge> merged, IReadOnlyList<RoofFootprint> sources, double toleranceFeet)` — each merged edge gets the slope of the source edge containing its midpoint.

- [ ] **Step 1: Write failing tests** (all in feet; helper `Rect(x0,y0,x1,y1)` builds a footprint)
  - `UnionTests.TwoOverlappingRects_GiveOneOuterLoop_AndOverlapIssue`: rects (0,0,10,10) and (5,0,15,10) -> `Outer.Count==1`, `Issues` contains `Overlap`, `CanCreate==true`.
  - `UnionTests.SharedFullEdge_IsJoined_NoIssues`: (0,0,10,10),(10,0,20,10) -> one outer, `Issues.Count==0`.
  - `UnionTests.RingOfFourRoofs_GivesOneOuterAndOneHole`.
  - `UnionTests.PointContactOnly_IsNotJoined`: (0,0,10,10),(10,10,20,20) -> `NotJoined`, `CanCreate==false`.
  - `UnionTests.IdenticalRoofs_AndNestedRoof_GiveSingleOuter`.
  - `UnionTests.RoofHoles_ArePreserved`.
  - `AnalyzerTests.GapBelowTolerance_IsSnapped_WithGapLink`: gap 0.01 ft, tol 0.0164 (5 mm) -> one outer, `Issues` has `Gap`, `GapLinks.Count>=1`, `CanCreate==true`.
  - `AnalyzerTests.GapAboveTolerance_IsNotJoined`; `GapEqualToTolerance_Snaps`; `ToleranceZero_SnapsNothing`.
  - `AnalyzerTests.CrossingBoundaries_ReportIntersectionAtCrossPoint` (L-shape crossing rect; location within 1e-6 ft).
  - `AnalyzerTests.ShortSegmentsBelow0_00256ft_AreRemoved`.
  - `SlopeMapperTests.MergedEdge_TakesSlopeOfContainingSourceEdge`: roof A south edge slope 0.1, roof B south edge slope 0.2 -> merged south edge halves carry 0.1 and 0.2 respectively.
- [ ] **Step 2: Run** `dotnet test Tests/MergeRoofs.Tests` — expect FAIL (types missing).
- [ ] **Step 3: Implement** models, `RoofUnion` (Clipper2 `Clipper.Union` with `FillRule.NonZero`, scale coordinates by 1e6 to `Path64`), `MergeAnalyzer` (flatten arcs for the union only, detect overlap/intersection pre-union via segment intersection and area of pairwise intersection, snap gaps by inflating each footprint by tolerance/2 then deflating, and record `GapLink`s between the nearest endpoints; classify leftovers as `NotJoined`; drop segments < 0.00256 ft), and `SlopeMapper` (midpoint-on-segment test within tolerance, otherwise slope not defined).
- [ ] **Step 4: Run tests** — expect all PASS.
- [ ] **Step 5: Commit** `feat(merge-roofs): core footprint union and analysis`

### Task 2: Arc re-fit

**Files:**
- Create: `Menu/RoofTools/Modelling/MergeRoofs_V001/Core/Services/ArcRefitter.cs`
- Test: `Tests/MergeRoofs.Tests/ArcRefitterTests.cs`

**Interfaces:**
- Consumes: `Edge`, `Loop`, `RoofFootprint` (Task 1).
- Produces: `static Loop ArcRefitter.Refit(Loop flattened, IReadOnlyList<RoofFootprint> sources, double toleranceFeet)` — runs of consecutive segments whose points all lie on one source arc (within tolerance) are replaced by one `Edge` with `ArcMid`; returns the input unchanged when nothing matches.

- [ ] **Step 1: Write failing tests**
  - `FlattenedHalfCircle_IsRefitToSingleArcEdge`: source roof with a 180-degree arc edge, flattened loop -> result has exactly one edge with `ArcMid` within tolerance of the true midpoint.
  - `StraightEdges_AreNeverConvertedToArcs`.
  - `NoMatchingArc_ReturnsLoopUnchanged`.
- [ ] **Step 2: Run** `dotnet test Tests/MergeRoofs.Tests --filter ArcRefitterTests` — FAIL.
- [ ] **Step 3: Implement** `ArcRefitter.Refit`: for each source arc (circle from A, ArcMid, B), collect consecutive segment runs on that circle and replace each run by one arc edge keeping the run's endpoints.
- [ ] **Step 4: Run** same command — PASS.
- [ ] **Step 5: Commit** `feat(merge-roofs): arc re-fit`

### Task 3: Revit adapters

**Files:**
- Create: `.../MergeRoofs_V001/Infrastructure/Helpers/{FootprintReader.cs,RoofBuilder.cs,DetailDrawer.cs,SelectionHelper.cs}`

**Interfaces:**
- Consumes: Task 1 and 2 types.
- Produces:
  - `static IReadOnlyList<FootPrintRoof> SelectionHelper.GetRoofs(UIDocument uidoc)` — pre-selected footprint roofs, else `PickObjects` with a roof filter; throws `InvalidOperationException` with a clear message when fewer than 2 or when one is read-only/checked out by another user.
  - `static RoofFootprint FootprintReader.Read(FootPrintRoof roof)` — from `roof.GetProfiles()`, outer = largest-area loop, others = holes; reads `get_DefinesSlope`/`get_SlopeAngle` per edge.
  - `static (FootPrintRoof Roof, bool UsedArcs) RoofBuilder.Create(Document doc, FootPrintRoof template, MergeResult merged, IReadOnlyList<RoofFootprint> sources, double toleranceFeet)` — inside one `Transaction`: build the `CurveArray`s from `ArcRefitter.Refit` loops, call `doc.Create.NewFootPrintRoof` with the template's `RoofType` and `Level`, apply `set_DefinesSlope`/`set_SlopeAngle` from `SlopeMapper`; on any exception roll back and retry once with flattened segments; if the retry fails, rethrow.
  - `static void DetailDrawer.DrawProfile(Document doc, View view, MergeResult merged)`, `DrawGapLinks(doc, view, IReadOnlyList<GapLink>)`, `DrawCircles(doc, view, IReadOnlyList<Issue>)` — detail curves in the active view; circle radius 0.5 ft (about 150 mm), each in its own `Transaction`.

- [ ] **Step 1:** Implement the four helpers per the signatures above, each `///`-documented. No Revit-free test is possible for these; verify by compile.
- [ ] **Step 2: Build** `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` — 0 errors.
- [ ] **Step 3: Commit** `feat(merge-roofs): revit adapters`

### Task 4: Window, view model and command

**Files:**
- Create: `.../MergeRoofs_V001/UI/ViewModels/MergeRoofsViewModel.cs`, `UI/Views/MergeRoofsWindow.xaml(.cs)`, `Commands/MergeRoofsCommand.cs`, `Core/Models/MergeSettingsStore.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces: `MergeRoofsCommand : IExternalCommand` (`[Transaction(TransactionMode.Manual)]`, delegates to `ToolGuard.RunCommand`); `MergeRoofsViewModel` with `ToleranceMm` (default 5), `PlaceGapLinks` (checkbox "Place connecting lines across gaps"), `Roofs` (list), `Issues` (list), `Status` (text), `CheckCommand`, `CreateCommand` (enabled only after a Check with `CanCreate`).

- [ ] **Step 1:** Copy the window shell pattern from `IslandRoofSplitterWindow.xaml` (shared styles and `ToolWindowShell`; title from `ToolCatalog.MergeRoofs`); labels exactly "Gap tolerance (mm)", "Place connecting lines across gaps", "Check", "Create roof".
- [ ] **Step 2:** `CheckCommand` runs `MergeAnalyzer.Analyze` and fills `Issues`/`Status`; `CreateCommand` calls `RoofBuilder.Create` (then `DrawGapLinks` if ticked); the status states "arcs kept" or "arcs flattened".
- [ ] **Step 3:** On failure, `TaskDialog` "Place the profile as detail lines in the current view?" -> `DrawProfile`; then a separate `TaskDialog` "Mark issue areas with circles?" -> `DrawCircles`. A note shows when roofs differ in level or type.
- [ ] **Step 4:** Load/save `MergeSettingsStore` (versioned JSON; unreadable file -> defaults, logged).
- [ ] **Step 5: Build** — 0 errors. **Commit** `feat(merge-roofs): window and command`

### Task 5: Wiring, installer, docs and preview

**Files:**
- Modify: `Shared/Services/ToolCatalog.cs` (add `MergeRoofs`, "Merge Roofs", "1.0.0", "V001", "Revit26_Plugin_Revised.MergeRoofs.V001")
- Modify: `Menu/00_Push_Button_Menu_items/Ribbon/RoofToolsRibbon.cs` (Roof Edit panel: add a stacked button `Btn_MergeRoofs_V001`)
- Modify: `Revit26_Plugin_Revised.csproj` (`<Version>6.40.0</Version>`, `Clipper2` PackageReference); `Installer/Output/*.iss` (ship `Clipper2Lib.dll` where other dependency DLLs are listed)
- Modify: `CHANGELOG.md`, `README.md` Ribbon section, `Claude/Revit26_Plugin_Md Files/Claude/RIBBON_AND_ICONS.md`
- Create: `Claude/Previews/MergeRoofs_V001_Window.html`

- [ ] **Step 1:** Add the catalog entry and the ribbon button (icon: reuse an existing roof icon from `Icons`; tooltip `ToolCatalog.MergeRoofs.Tip(...)`).
- [ ] **Step 2:** Update csproj, installer scripts, CHANGELOG (Added, with a user note: originals are kept), README and RIBBON_AND_ICONS tables.
- [ ] **Step 3:** Write the preview per CLAUDE.md "UI previews" (Artifact format, tokens from `Shared/SharedStyles.xaml`, sample data labelled, green tag on new items; show window with the issues list, the gap checkbox and the failure prompts), then publish it as an Artifact, put the link in the header comment and open it.
- [ ] **Step 4: Verify** `dotnet test Tests/MergeRoofs.Tests` all PASS, and `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` shows 0 errors.
- [ ] **Step 5: Commit** `feat(merge-roofs): ribbon, docs, preview`; push; open a draft PR.
