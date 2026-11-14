# Wet Area Waterproofing — V001

Finds wet rooms in the host model and its links, repairs their outlines and adds a **waterproofing roof on
top of the slab** under each one. Writes an Excel report of every run.

Built in three parts. **Part A (this release):** rooms, outline repair, roofs, report, remove a run.
Part B: plans, detail sections, drafting views. Part C: grouping, sheets, print set.

| Item | Value |
|---|---|
| Tool name | Wet Area Waterproofing, v1.0.0 (plugin 7.11.0) |
| Folder | `Menu/Sheet/WetAreaWaterproofing_V001/` |
| Namespace | `Revit26_Plugin_Revised.WetAreaWaterproofing.V001` |
| Command | `…V001.Commands.WetAreaWaterproofingCommand` (via `ToolGuard.RunCommand`) |
| Ribbon | **Sheet Create › Wet Area Waterproofing** (`Btn_WetAreaWaterproofing_V001`) |
| Catalog | `ToolCatalog.WetAreaWaterproofing` |
| Settings | `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\settings.json` (with a `Version` field) |
| Reports | `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\Reports\WP_Report_<Model>_<yyyyMMdd_HHmm>.xlsx` |
| Tests | `dotnet test Tests/WetAreaWaterproofing.Tests` |
| Design | `docs/superpowers/specs/2026-11-14-wet-area-waterproofing-design.md` |
| Preview | `Claude/Previews/WetAreaWaterproofing_V001_Window.html` |

## Code layout

| Folder | Job |
|---|---|
| `Core/Matching` | `KeywordMatcher` — is a room name a wet area |
| `Core/Geometry` | `LoopRepair` — the outline repair chain; `Polyline2D`, `Hull` |
| `Core/Grid` | `GridQuery`, `GridState` — filters, sort, level buttons |
| `Core/Run` | `RunPlanner` (what a run does per room), `RunTag` (Comments text), `RoomStatusRules` |
| `Core/Report` | `RunReport`, `ReportWriter` — the Excel workbook |
| `Infrastructure` | Revit: `RoomCollector`, `BoundaryReader`, `SlabIndex`, `RoofCreator`, `RunRemover`, the session and its ExternalEvent handler |
| `UI` | `WetAreaWaterproofingWindow`, `ViewModel_WetAreaWaterproofing` (+ `.Grid`, `.Run`) |

`Core` has no Revit references and is covered by the unit tests. Lengths in `Core` are millimetres.

## How elements are tracked

Every roof gets `WP|<RunId>|<RoomKey>|<Number>|<Name>` in **Comments** (`RunId` = `yyyyMMdd-HHmmss`,
`RoomKey` = `Host:<room UniqueId>` or `<link instance UniqueId>:<room UniqueId>`). A later run finds the roof by
its room key and replaces it; *Remove a run* finds elements by run ID. The format is append-only.

## Excel report

Sheets: Summary, Rooms, Boundary repair, Issues. Column names and order are fixed (`ReportWriter.RoomColumns`
and friends); new columns are added at the end only. `Group` and `Scale` are filled from part C on.

## Known limits

- Curved boundaries are faceted (the repair chain works on straight pieces).
- A slab is looked for within 500 mm above or below the room's level.
- Needs testing in Revit on: a rotated link, a workshared model, rooms with columns inside.
