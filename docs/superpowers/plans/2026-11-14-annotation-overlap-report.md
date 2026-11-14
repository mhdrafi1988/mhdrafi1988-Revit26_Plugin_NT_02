# Annotation Overlap Report Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A report-only Revit tool that lists overlapping annotations of every type across a batch of views and sheets, with zoom-to-pair and Excel export.

**Architecture:** One tool folder with `Commands`, `Core`, `UI`, `Infrastructure`. `Core` is pure C# (boxes in paper mm, sweep overlap finder, report rows) and is unit-tested. `Infrastructure` holds everything that touches Revit (collection, scan and zoom external events), Excel and settings. The modeless WPF window talks to Revit only through `ExternalEvent`s.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, Revit 2026 API, ClosedXML (already referenced, MIT), xunit.

**Spec:** `docs/superpowers/specs/2026-11-14-annotation-overlap-report-design.md` (approved 2026-11-14, Q1 answered: option a).

## Global Constraints

- Folder `Menu/Manage/AnnotationOverlapReport_V001/`; namespace `Revit26_Plugin_Revised.AnnotationOverlapReport.V001`.
- Tool name "Annotation Overlap Report", version 1.0.0, build tag V001; plugin `<Version>` 7.2.1 → 7.3.0.
- No transaction is ever opened; the command is `TransactionMode.ReadOnly`. V005/V006 files are not edited.
- Every `IExternalCommand.Execute` / `IExternalEventHandler.Execute` delegates to `ToolGuard.RunCommand` / `RunHandler`.
- `///` comments on all public types and members.
- Overlap = min(horizontal depth, vertical depth) in paper mm (model mm ÷ view scale); touching boxes are not overlaps.
- Excel columns, fixed order: Sheet, View, Scale, Type A, Text A, ID A, Type B, Text B, ID B, Overlap (mm). Format version 1.
- Settings: `%AppData%\Revit26_Plugin_Revised\AnnotationOverlapReport\settings.json`, `Version = 1`.
- Head categories (grids, levels, sections, elevations, callouts, viewports/view titles) are listed greyed out as "coming in 1.1" and never checked.
- Build: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` with 0 errors.

## Review Focus

1. Boxes that only touch (shared edge, float noise) must not be reported → Task 1 test `Touching_boxes_are_not_overlaps`.
2. One box fully inside another → depth is the smaller box's smaller escape distance, never negative or zero → Task 1 test `Contained_box_reports_positive_depth`.
3. Many annotations in one view (thousands) must not hang → sweep, Task 1 test `Finds_same_pairs_as_brute_force` on 2,000 random boxes.
4. Text with line breaks or a leading `=` must land in Excel as plain text, one row per pair → Task 2 test `Text_is_written_as_plain_single_line_string`.
5. No views ticked, or no pairs found → no crash; status line says so, Export disabled → Task 4 view-model guards (manual check in Revit).

---

### Task 1: Core geometry and overlap finder

**Files:**
- Create: `Menu/Manage/AnnotationOverlapReport_V001/Core/PaperBox.cs`, `Core/OverlapPair.cs`, `Core/OverlapFinder.cs`
- Test: `Tests/AnnotationOverlapReport.Tests/AnnotationOverlapReport.Tests.csproj` (files linked, as in `Tests/AnnotationOverlapDetection.Tests`), `OverlapFinderTests.cs`

**Interfaces — Produces:**
- `sealed class PaperBox { long Id; string TypeName; string Text; double X, Y, Width, Height; double Right => X+Width; double Top => Y+Height; }` (paper mm).
- `sealed class OverlapPair { PaperBox A; PaperBox B; double OverlapMm; }` (A is the one with the smaller Id).
- `static class OverlapFinder { const double MinOverlapMm = 0.01; static List<OverlapPair> Find(IReadOnlyList<PaperBox> boxes, bool acrossTypes); static double ModelMmToPaperMm(double modelMm, int viewScale); }` — result sorted by `OverlapMm` descending, rounded to 2 decimals; `viewScale < 1` is treated as 1.

- [ ] Write tests: `Touching_boxes_are_not_overlaps`, `Overlap_is_smaller_of_two_depths` (boxes 0,0,10,5 and 8,3,10,5 → 2.0), `Contained_box_reports_positive_depth`, `Same_type_only_when_acrossTypes_false`, `Each_pair_once_smaller_id_first`, `Finds_same_pairs_as_brute_force`, `ModelMmToPaperMm_divides_by_scale` (180 at 1:100 → 1.8; scale 0 → 180).
- [ ] Run `dotnet test Tests/AnnotationOverlapReport.Tests` → fails to compile.
- [ ] Implement (sort by X, sweep with an active list pruned by `Right`).
- [ ] Run tests → pass. Commit.

### Task 2: Report rows and Excel writer

**Files:**
- Create: `Core/ReportRow.cs`, `Core/ReportRunInfo.cs`, `Infrastructure/ExcelReportWriter.cs`
- Test: `Tests/AnnotationOverlapReport.Tests/ExcelReportWriterTests.cs` (adds ClosedXML 0.105.0 to the test project)

**Interfaces — Produces:**
- `sealed class ReportRow { long ViewId; string Sheet; string View; string Scale; string TypeA; string TextA; long IdA; string TypeB; string TextB; long IdB; double OverlapMm; }`
- `sealed class ReportRunInfo { string ToolVersion; DateTime RunAt; int ViewsScanned; int AnnotationsScanned; int NotMeasured; List<string> SkippedViews; }`
- `static class ExcelReportWriter { const int FormatVersion = 1; static readonly string[] Columns; static void Write(string path, IReadOnlyList<ReportRow> rows, ReportRunInfo info); static string Clean(string text); }` — sheets "Overlaps" and "Run info"; `Clean` collapses whitespace/newlines to single spaces; text cells are set as strings (never formulas).

- [ ] Write tests: `Header_row_has_the_ten_columns_in_order`, `One_row_per_pair`, `Text_is_written_as_plain_single_line_string` (`"=SUM(1)\nLINE"` → cell text `=SUM(1) LINE`, no formula), `Run_info_has_format_version_1`.
- [ ] Run → fail. Implement. Run → pass. Commit.

### Task 3: Revit infrastructure

**Files (all under `Infrastructure/`):** `ViewCatalog.cs`, `AnnotationCollector.cs`, `ScanRequest.cs`, `ScanEventHandler.cs`, `ZoomEventHandler.cs`, `ReportSettings.cs`

**Interfaces — Produces:**
- `sealed class ViewEntry { long Id; string Name; string Group ("Sheets"/"Views"); string ScaleText; }`
- `static class ViewCatalog { static bool IsSupported(View v); static List<ViewEntry> List(Document doc); static List<View> Expand(Document doc, IEnumerable<long> chosenIds); }` — `Expand` returns each sheet followed by the supported views placed on it, de-duplicated, sheets before plain views.
- `enum CategoryKind { Checked, Deferred, Ignored }`
- `sealed class ViewScan { View view data: long ViewId; string ViewName; string SheetNumber; int Scale; List<PaperBox> Boxes; Dictionary<string,int> DeferredCounts; int NotMeasured; }`
- `static class AnnotationCollector { static CategoryKind Classify(Category c); static ViewScan Collect(Document doc, View view); static string TextOf(Element e); }` — `Checked` = `CategoryType.Annotation` minus the ignored list (title blocks, reference planes, scope boxes, section box, crop boundaries, matchlines, cameras, schedule graphics, view references' regions) and minus Deferred (grids, levels, section/elevation/callout heads, viewports). Box projection as V006 `BoundingBoxCalculator.GetBoundingBox`, then `OverlapFinder.ModelMmToPaperMm`. `TextOf`: `TextNote.Text`, `IndependentTag.TagText`, `SpatialElementTag.TagText`, `Dimension.ValueString`, else element name; every read in try/catch → empty string.
- `sealed class ScanRequest { bool CountOnly; bool ActiveViewOnly; List<long> ViewIds; HashSet<string> Types (null = all); bool AcrossTypes; }`
- `sealed class ScanOutcome { List<ReportRow> Rows; Dictionary<string,int> TypeCounts; Dictionary<string,int> DeferredCounts; ReportRunInfo Info; int ViewsWithOverlaps; string Error; }`
- `ScanEventHandler : IExternalEventHandler { ScanRequest Request; Action<ScanOutcome> Completed; Action<int,int,string> Progress; }` — per-view try/catch: a failing view is added to `Info.SkippedViews`; `ToolCancellation.CheckCurrent()` per view.
- `ZoomEventHandler : IExternalEventHandler { long ViewId; long IdA; long IdB; Action<string> Failed; }` — activates the view (`uiDoc.RequestViewChange` is async, so set `uiDoc.ActiveView` inside the handler), `ShowElements`, selects both.
- `sealed class ReportSettings { int Version = 1; List<string> UncheckedTypes; bool AcrossTypes = true; string LastExportFolder; }` loaded/saved with `JsonFileStore.PathFor("AnnotationOverlapReport", "settings.json")`.

- [ ] Implement; build main project → 0 errors. Commit. (No unit tests: Revit types cannot load outside Revit.)

### Task 4: Window, command, catalog, ribbon, docs

**Files:**
- Create: `Commands/AnnotationOverlapReportCommand.cs`, `UI/AnnotationOverlapReportWindow.xaml(.cs)`, `UI/AnnotationOverlapReportViewModel.cs`, `UI/ViewRow.cs`, `UI/TypeRow.cs`
- Modify: `Shared/Services/ToolCatalog.cs` (entry `AnnotationOverlapReport` after `AnnotationOverlapDetectionV006`), `Menu/00_Push_Button_Menu_items/Ribbon/ManageRibbon.cs` (button `Btn_AnnotationOverlapReport_V001`, text "Overlap Report", icon `Manage.AnnotationOverlapDetection_16.png`, placed right after the Overlaps pulldown in the visible stack), `Revit26_Plugin_Revised.csproj` (`<Version>7.3.0`), `CHANGELOG.md`, `README.md` ribbon row, `Claude/Revit26_Plugin_Md Files/Claude/RIBBON_AND_ICONS.md`, `Claude/Previews/AnnotationOverlapReport_V001_Window.html`

**Window** (follows the mock-up; `ToolWindowShell`, `SharedStyles.xaml`): Scope card, Annotation types card (deferred rows disabled with "coming in 1.1"), "Also check overlaps between different types" (default on), Check overlaps; tiles Overlaps / Views with overlaps / Scanned / Types checked; Show filters; grid grouped by view; status line; Export to Excel (disabled with no rows), Zoom to pair (disabled with no selection; also on row double-click), Close.

- [ ] Implement view model: scope change → count-only scan; Check → full scan under `ToolCancellation`; guards for no views ticked / no types ticked / no document.
- [ ] Wire command (`TransactionMode.ReadOnly`, creates both `ExternalEvent`s, shows the window modeless), catalog, ribbon, version, docs.
- [ ] Build → 0 errors; `dotnet test Tests/AnnotationOverlapReport.Tests` → pass.
- [ ] Update the preview to mirror the real XAML labels, republish to the same Artifact link. Commit, push.
