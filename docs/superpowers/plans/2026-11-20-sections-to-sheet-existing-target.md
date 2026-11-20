# Sections To Sheet — Existing-Sheet Target Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Sections To Sheet place sections on an existing sheet, in its free space or re-arranging every view on it.

**Architecture:** Revit-free models + pure free-space finder + planner extension (tested with xUnit); a Revit-side
reader for the target sheet; runner, session, view model, XAML and preview canvas follow the plan.

**Tech Stack:** C# / .NET 8, WPF, Revit 2026 API, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-20-sections-to-sheet-existing-target-design.md`

## Global Constraints

- Tool `ToolCatalog.SectionsToSheet` 1.1.0 → **1.2.0**; plugin `<Version>` 7.27.2 → **7.28.0**.
- New options default to old behaviour: `Target = NewSheet`, `ExistingMode = FreeSpace`; settings `CurrentVersion = 2`, version-1 files load.
- No renamed command classes, button names or namespaces. Model changes in `Transaction`/`TransactionGroup`, rolled back on any error.
- `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` → 0 errors; `dotnet test Tests/SectionsToSheet.Tests` green.
- CHANGELOG entry with user note; preview republished to https://claude.ai/artifact/HMYzMyc95SF4zyDxxZjHTQ.

## Review Focus

1. Target sheet already full (no free rectangle) → free space: all sections overflow to new sheets (or are skipped); no crash. Test in Task 2.
2. A ticked section already on the target sheet → not moved again, not deleted, shown as a note (not "can't place"). Test in Task 2.
3. Non-numeric existing detail numbers ("A3") → new numbers continue from the highest numeric one (or 1). Test in Task 2.
4. Re-arrange where an existing view is bigger than the free area → blocked with a clear message, nothing changes. Test in Task 2.
5. Target sheet or one of its viewports owned by another user → blocked / viewport kept as an obstacle with a warning. Test in Task 2.

---

### Task 1: Models, settings and the free-space finder

**Files:** Modify `Core/Models/Enums.cs`, `Core/Models/SectionsToSheetSettings.cs`, `Core/Models/SourceSnapshot.cs`;
Create `Core/Models/TargetSheetContent.cs`, `Core/Layout/FreeSpace.cs`; Test `Tests/SectionsToSheet.Tests/FreeSpaceTests.cs`.

**Produces:**
- `enum TargetMode { NewSheet, ExistingSheet }`, `enum ExistingSheetMode { FreeSpace, Rearrange }`.
- Settings: `TargetMode Target`, `ExistingSheetMode ExistingMode`, `string TargetSheetNumber`.
- `record SheetChoice(long Key, string Number, string Name)` with `Label` = "Number - Name"; `SourceSnapshot.AllSheets`.
- `TargetItem { long Key; long ViewKey; string Name; bool IsViewport; LayoutRect Rect; double BoxCenterX, BoxCenterY; string DetailNumber; int Scale; string LockReason }`.
- `TargetSheetContent { long Key; string Number; string Name; string TitleBlockName; LayoutRect? Frame; string FrameProblem; string LockReason; List<TargetItem> Items }`.
- `static LayoutRect? FreeSpace.Largest(LayoutRect area, IEnumerable<LayoutRect> obstacles)`; `LayoutRect.Intersects`, `Grow`.

- [ ] Tests: empty sheet → whole area; obstacle top-left → the larger of the strips; area fully covered → null; ties prefer higher then further left.
- [ ] Implement: candidate lefts = area.MinX ∪ obstacle MaxX, rights = area.MaxX ∪ obstacle MinX; per pair, merge y-intervals of obstacles overlapping the x-range, take the largest y-gap.
- [ ] Settings test: version-1 JSON loads with `Target = NewSheet`; `Normalize` resets unknown enum values.
- [ ] Commit.

### Task 2: Planner on an existing sheet

**Files:** Modify `Core/Services/SectionPlanner.cs`; Test `Tests/SectionsToSheet.Tests/ExistingTargetTests.cs`.

**Consumes:** Task 1. **Produces:** `PlanRequest.Target` (TargetSheetContent); `PlannedSheet.ExistingKey`, `IsExisting`,
`Frame`, `Area`, `FreeArea`, `Obstacles` (`List<TargetItem>`); `PlannedViewport.ExistingViewportKey`, `ExistingDetail`,
`IsRearranged`, `DetailLabel`, `Name`, `Scale`, `IsMove` (new view off another sheet only); `SkippedSection.IsNote`;
`SectionPlan.NewSheetCount`, `RearrangedCount`.

- [ ] Tests: free space keeps all items, lays new sections inside the free rect, detail numbers from highest+1;
  re-arrange: existing first in reading order, keep detail numbers, schedules stay; overflow → new sheet numbered from FirstSheetNumber;
  Review Focus 1–5; signature differs between modes and targets.
- [ ] Implement: page 0 = target (single page in free rect); leftovers → today's layout on the new-sheet frame.
- [ ] Run all tests; commit.

### Task 3: Revit side — reader, runner, session

**Files:** Create `Core/Services/TargetSheetReader.cs`; Modify `Core/Services/FootprintMeasurer.cs` (shared outline helper),
`Core/Services/PlacementRunner.cs`, `Core/Services/SectionCollector.cs` (AllSheets), `Infrastructure/ISectionsSession.cs`, `Infrastructure/RevitSectionsSession.cs`.

- [ ] `TargetSheetReader.Read(Document, long sheetKey) → TargetSheetContent`: title block bbox, viewports (box ∪ label, centre, detail, locks), other owned elements' bboxes (skip title blocks, viewports, revision schedules, guide grids).
- [ ] Runner: existing sheet → no create; re-arranged viewports `SetBoxCenter` (unpin/re-pin); number only new viewports. `RunResult.TargetSheet`, `Rearranged`.
- [ ] Session: `ReadTarget(long, done)`; `Refresh` and `Apply` re-read the target; `ApplyRequest.TargetSheetKey`; `SessionResult.Target`; `InitialTarget`.
- [ ] Build; commit.

### Task 4: Window, preview, docs

**Files:** `UI/ViewModels/SectionsToSheetViewModel.cs`, `UI/Views/SectionsToSheetWindow.xaml(.cs)`, ribbon tooltip,
`ToolCatalog.cs`, `.csproj`, `CHANGELOG.md`, tool `README.md`, preview HTML.

- [ ] Target Sheet card, sheet picker, mode chips, button text, confirm text; canvas draws obstacles, re-arranged views, free area.
- [ ] Versions, CHANGELOG, README, preview; build 0 errors, tests green; commit, push, PR, publish preview.
