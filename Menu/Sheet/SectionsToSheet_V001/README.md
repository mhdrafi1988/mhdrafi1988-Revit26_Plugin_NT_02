# Sections To Sheet — V001

Takes the sections shown in a view (or the selected / picked ones) **off the sheet they are on** and lays
them out on a **new sheet**, aligned and evenly spaced, with a live preview before anything changes.

| Item | Value |
|---|---|
| Tool name | Sections To Sheet, v1.0.0 (plugin 6.16.0) |
| Folder | `Menu/Sheet/SectionsToSheet_V001/` |
| Namespace | `Revit26_Plugin_Revised.SectionsToSheet.V001` |
| Command | `Revit26_Plugin_Revised.SectionsToSheet.V001.Commands.SectionsToSheetCommand` (via `ToolGuard.RunCommand`) |
| Ribbon | **SloperPro_Rev_88-DCM › Sheet Create › Sections To Sheet** (`Btn_SectionsToSheet_V001`) |
| Catalog | `ToolCatalog.SectionsToSheet` |
| Settings | `%AppData%\Revit26_Plugin_Revised\SectionsToSheet\settings.json` (with a `Version` field) |
| Tests | `dotnet test Tests/SectionsToSheet.Tests` |

## Code layout

| File | Job |
|---|---|
| `Commands/SectionsToSheetCommand.cs` | Checks the document and view, loads settings, opens the window |
| `Core/Layout/SheetGridLayout.cs` | Grid / Rows layout, sheets, vertical placement (pure, unit-tested) |
| `Core/Layout/ReadingOrder.cs` | Plan order of markers in row bands; natural name sort (pure) |
| `Core/Layout/SheetNumbering.cs` | Next free sheet number, numbers for extra sheets (pure) |
| `Core/Services/SectionPlanner.cs` | Sections + sizes + settings → plan: sheets, moves, skips, signature (pure) |
| `Core/Services/SectionCollector.cs` | Finds section markers in the view / selection; current sheet; worksharing locks |
| `Core/Services/FootprintMeasurer.cs` | Measures view + title sizes on a temporary sheet (rolled back) |
| `Core/Services/PlacementRunner.cs` | The run: off old sheets, new sheets, viewports, detail numbers, empty sheets |
| `Infrastructure/*` | External event handler (via `ToolGuard.RunHandler`) and the session the window talks to |
| `UI/*` | Window (shared `ToolWindowShell`), view model, converters |

## How it works

1. **Source** — section markers (category *Views*, "viewers") shown in the active view are mapped to their
   section views through each view's dependent elements. Also: the Revit selection (markers, or section views
   in the Project Browser) and **Pick in view…** (the window steps aside while you pick). Section and
   detail-section views are offered; templates are not.
2. **Measure** — each view is placed on a temporary sheet with the chosen title block and viewport type, inside
   a transaction that is always rolled back. Its footprint is the viewport box united with its title, plus where
   the box centre sits in it. The title block's extent gives the sheet frame. Views now on a sheet are measured
   after being taken off it, as they will be.
3. **Plan** (pure, same code for preview and run) — order the views, then lay them out in the usable area (frame
   inset by the margins):
   - **Grid**: the most columns that fit at the minimum gap; each column as wide as its widest view; columns
     spread to the full width, so columns line up from row to row.
   - **Rows**: rows fill like text; each row spread to the full width; a single view is centred; a short last row
     goes left / centre / stretched, keeping the gap of the row above.
   - Views in a row share a bottom line (titles line up). Rows from the top, centred, or spread to fill the
     height. Rows that don't fit go on the next sheet (or are skipped and reported).
4. **Run** — one transaction group: the session re-reads the model, re-measures and re-plans; if the plan's
   signature differs from the preview, nothing is changed. Otherwise one transaction removes the old
   viewports, creates the sheets, places the viewports at the planned box centres, sets the viewport type and
   detail numbers 1…n, and deletes old sheets left empty (if asked). Any error rolls everything back.

## Robustness

- Sections whose view, viewport or old sheet is owned by another user, or changed in central, are skipped with
  the reason (`WorksharingUtils.GetCheckoutStatus` / `GetModelUpdatesStatus`).
- Views Revit can't put on a sheet, views bigger than the usable area and views that could not be measured are
  skipped with the reason; the sheet number is checked against every sheet in the model.
- Warnings in the tool's transactions are dismissed; errors roll the transaction back and are reported.
- The window can't close while Revit is working on a request. Unexpected errors go to
  `%AppData%\Revit26_Plugin_Revised\Logs` through `ToolGuard.Report`.

## Needs checking in Revit

Built and unit-tested on Linux; not yet run in Revit. Check these on a real model:

1. "All in view" lists the section markers of a plan (and of an elevation / section view).
2. Picking markers works and the window comes back after Finish / Esc.
3. Moving a section off a sheet keeps its view, marker and annotations; section heads show the new sheet.
4. Titles line up along each row with your viewport type; detail numbers follow the order.
5. Default margins (30 / 150 / 30 / 200 mm, as in Sheet View Arrange) suit your title block.
