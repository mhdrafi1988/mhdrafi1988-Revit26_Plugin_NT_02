# Annotation Overlap Report — design

Status: **approved by the owner on 2026-11-14 (Q1: option a). Version 1.0.0 is implemented; it awaits the owner's test in Revit.**
Date: 2026-11-14 · Target: Revit 2026, .NET 8, WPF, plugin `Revit26_Plugin_Revised`.

UI mock-up: `Claude/Previews/AnnotationOverlapReport_V001_Window.html`, published at
https://claude.ai/artifact/9ZhpmxsCCcUjZTNKbqE23j.

## 1. What it is and why

A report-only tool that scans a batch of views and sheets and lists every pair of overlapping annotations,
so drawings can be checked for clashing labels before issue without opening each view. It changes nothing in
the model. Moving annotations apart stays the job of Annotation Overlap Detection V006.

## 2. Decisions

**Decided by the owner**

| # | Decision |
|---|---|
| D1 | A separate tool, not Annotation Overlap Detection V007. V006 and V005 are not touched. |
| D2 | Overlap only. No minimum-distance, alignment or distribution checks. |
| D3 | Every annotation type found in the chosen views is listed in the type list, not a fixed set. |
| D4 | Scope: the active view, or a chosen batch of views and sheets. |
| D5 | Report only. No fix or move. |
| D6 | Name **Annotation Overlap Report**; ribbon button **Overlap Report** next to the Overlaps pulldown. |
| D7 | "Also check overlaps between different types" is on by default. |
| D8 | Excel export of the listed pairs. |

**Claude's calls** (not yet confirmed by the owner)

- C1. Tool version 1.0.0, folder `Menu/Manage/AnnotationOverlapReport_V001`, namespace
  `Revit26_Plugin_Revised.AnnotationOverlapReport.V001`. Plugin 7.4.0 → 7.5.0 (new feature).
- C2. The type list is grouped by Revit category name (Door Tags, Text Notes, Dimensions, Spot Elevations,
  Generic Annotations, ...), so new categories appear without code changes.
- C3. Overlap is measured as the smaller of the two overlap depths (horizontal, vertical), in millimetres on
  paper (model distance ÷ view scale). Boxes that only touch are not overlaps.
- C4. A sheet is checked as: annotations placed on the sheet itself, plus each view placed on it, each in its
  own coordinates. Overlaps between two different viewports on a sheet are not checked in 1.0.0.
- C5. A view chosen directly and also reached through its sheet is scanned once.
- C6. The ribbon button reuses the existing overlap icon until the owner supplies another.
- C7. Settings in `%AppData%\Revit26_Plugin_Revised\AnnotationOverlapReport\settings.json` with a `version`
  field (1): unticked types, the different-types option, last export folder.
- C8. Excel: one `.xlsx`, sheet "Overlaps" (columns below) and sheet "Run info" (tool version, format version 1,
  date, views scanned). Uses the library the existing exports use.

**Q1, answered by the owner: option (a). Datum and view-mark heads.** For grids, levels, section marks, elevation
marks, callout heads and view titles, Revit's bounding box covers the whole line or region, not just the head,
so a plain box test would report false overlaps along every grid line. Options:

- (a) *Recommended:* 1.0.0 lists and checks every annotation whose box is the label itself (all tags, text
  notes, dimensions, spots, generic annotations, keynotes, revision tags and similar). The head types appear in
  the type list greyed out with "coming in 1.1"; 1.1.0 adds them with head-only boxes.
- (b) Include heads in 1.0.0 with approximate head boxes computed from the datum end point and head family
  size. More work, and the boxes need tuning in Revit before the results can be trusted.

## 3. Goals, non-goals, anti-goals

- Goals: one run across many views; a list that is easy to act on (zoom to pair); an Excel record.
- Non-goals: moving annotations; overlaps with model geometry; annotations inside linked models (Revit does
  not expose them as host elements); overlaps between viewports on a sheet.
- Anti-goals: changing the model in any way; a list so full of false positives that it is ignored.

## 4. Approaches

1. **Own detection code in the new tool folder, adapted from V006 (chosen).** V006's box projection
   (`BoundingBoxCalculator.GetBoundingBox`) and view test (`ViewSupport`) are copied and adapted. The new tool
   needs different logic anyway (paper mm, any category, many views, sweep instead of all pairs), tool folders
   in this repo are self-contained, and V006 stays untouched (D1).
2. Move V006's helpers to `Shared/` and use them from both. Rejected: it edits V006 and couples two tools
   whose needs already differ.
3. Call V006's helpers directly from the new tool. Rejected: ties the new tool to a versioned folder that will
   one day be removed.

## 5. How it works

**Window (modeless).** As in the mock-up: Scope card (Active view / Chosen views and sheets with search and
tick list), Annotation types card (every category found, counts, All / None, different-types option),
Check overlaps button; summary tiles; Show filters (type, view); results grid grouped by view with
Annotation A, ID, Annotation B, ID, Overlap; status line; Export to Excel, Zoom to pair, Close.

**Flow.**
1. The command (`ToolGuard.RunCommand`) creates the `ExternalEvent`s and opens the window. The view/sheet list
   is read from the document: supported 2D views (same kinds as V006 `ViewSupport`), no templates.
2. Changing the scope triggers a light count pass (scan event) that fills the type list.
3. Check overlaps raises the scan event. Per view: collect elements in the view whose category is an
   annotation category and has a bounding box in that view, minus an exclusion list (title blocks, reference
   planes, scope boxes, crop and section boxes, matchlines, cameras, viewports, schedule graphics, and the head
   types under Q1(a)); project each box to view right/up axes; divide by view scale to get paper mm.
4. Detect pairs with a sort-and-sweep on X, then a Y test. With the different-types option off, only pairs of
   the same category are compared.
5. Results go back to the view model; the grid is grouped by view, sorted by overlap depth, largest first.
6. Zoom to pair (zoom event): activate the view, `ShowElements` both, select both.
7. Export to Excel writes the rows currently shown.

**Layers** (`Commands`, `Core`, `UI`, `Infrastructure`):

- `Core`: `PaperBox`, `OverlapPair`, `OverlapFinder` (sweep) — pure C#, no Revit, unit-tested in `Tests/`.
- `Infrastructure`: `AnnotationCollector` (Revit collection, exclusions, box projection),
  `ScanEventHandler`, `ZoomEventHandler` (both through `ToolGuard.RunHandler`), `ExcelReportWriter`,
  `SettingsStore`.
- `UI`: window XAML and view model (CommunityToolkit.Mvvm, `Shared/SharedStyles.xaml`).

**Excel columns** (fixed order; new columns are only ever appended): Sheet, View, Scale, Type A, Text A, ID A,
Type B, Text B, ID B, Overlap (mm).

## 6. Robustness

- Read-only: no transaction is opened. Command is `TransactionMode.ReadOnly`.
- The scan is cancellable (`ToolCancellation`), and reports progress per view in the status line.
- A view that fails or is unsupported is skipped and named in the status line and the Run info sheet; the run
  continues.
- Elements with no bounding box in a view (hidden, or not regenerated in a closed view) are counted as
  "not measured" per view and shown, never silently dropped. **To verify in Revit:** whether tag boxes are
  returned for views that are not open; if not, the scan opens no views but reports the count.
- Document closed or switched while the window is open: handlers check `IsValidObject` and the window closes
  with a message.
- Annotations inside groups are scanned as normal elements. Annotations in linked models are out of scope and
  the window says so.
- Excel file locked or path not writable: clear message, nothing lost; the list stays in the window.

## 7. Other changes in the same PR

- `Shared/Services/ToolCatalog.cs`: new entry `AnnotationOverlapReport` ("Annotation Overlap Report", 1.0.0, V001).
- `Menu/00_Push_Button_Menu_items/Ribbon/ManageRibbon.cs`: new button `Btn_AnnotationOverlapReport_V001`,
  label "Overlap Report", tooltip from `ToolCatalog`, beside the Overlaps pulldown.
- `RIBBON_AND_ICONS.md`, `README.md` ribbon section, `CHANGELOG.md`, `<Version>` 7.5.0.
- The preview page is updated to mirror the real XAML and republished to the same link.

## 8. Left to the builder

Exact XAML layout within the mock-up, class member names, the sweep implementation, the exclusion list's
final contents (extend when a category proves to give a box that is not its label), and test cases.

## 9. Testing

- Unit tests for `OverlapFinder` (touching boxes, contained boxes, same-type-only, scale conversion).
- Build with 0 errors. Running it needs Revit on Windows: the owner tests on a real model (a plan, a section,
  a sheet with several views), compares a few pairs by eye, and opens the Excel file.
