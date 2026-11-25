# Wet Area Waterproofing — V002

Finds wet rooms in the host model and its links, repairs their outlines and adds a **waterproofing roof on
top of the slab** under each one. Writes an Excel report of every run.

**V002 (2.0.0)** is V001 1.2.5 plus a searchable drafting-view assignment (see *Assigning drafting views*
below). It sits next to V001 in the ribbon pulldown and shares its settings file, Extensible Storage tag and
report folder.

Built in three parts. **Part A (1.0.0):** rooms, outline repair, roofs, report, remove a run.
**Part B (1.1.0):** a plan per room, up to 6 reference sections per room, drafting views.
Part C: grouping, sheets, print set.

| Item | Value |
|---|---|
| Tool name | Wet Area Waterproofing, v2.1.0 (plugin 7.31.0) |
| Folder | `Menu/Sheet/WetAreaWaterproofing_V002/` |
| Namespace | `Revit26_Plugin_Revised.WetAreaWaterproofing.V002` |
| Command | `…V002.Commands.WetAreaWaterproofingCommand` (via `ToolGuard.RunCommand`) |
| Ribbon | **Waterproofing › Wet Area Waterproofing ▾ › Wet Area Waterproofing V002** (`Btn_WetAreaWaterproofing_V002`, first in `Pulldown_WetAreaWaterproofing`) |
| Catalog | `ToolCatalog.WetAreaWaterproofingV002` |
| Settings | `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\settings.json` (with a `Version` field) |
| Reports | `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\Reports\WP_Report_<Model>_<yyyyMMdd_HHmm>.xlsx` |
| Tests | `dotnet test Tests/WetAreaWaterproofingV002.Tests` |
| Design | `docs/superpowers/specs/2026-11-14-wet-area-waterproofing-design.md` |
| Preview | `Claude/Previews/WetAreaWaterproofing_V002_Window.html` (also a flowchart and mind map of the whole tool) |

## Code layout

| Folder | Job |
|---|---|
| `Core/Matching` | `KeywordMatcher` — is a room name a wet area |
| `Core/Geometry` | `LoopRepair` — the outline repair chain; `Polyline2D`, `Hull` |
| `Core/Grid` | `GridQuery`, `GridState` — filters, sort, level buttons |
| `Core/Run` | `RunPlanner` (what a run does per room), `RunTag` (Comments text), `RoomStatusRules` |
| `Core/Sections` | `DraftingViewSearch` / `DraftingViewInfo` (the "Points to" search), `SectionTargets` (the table), `ItemClassifier` / `RoomItemMatcher` (which door, drain or fixture belongs to which room), `SectionPlanner` (which cuts, where, max 6), `ViewNames` |
| `Core/Report` | `RunReport`, `ReportWriter` — the Excel workbook |
| `Infrastructure` | Revit: `RoomCollector`, `BoundaryReader`, `SlabIndex`, `RoofCreator`, `RunRemover`, `TargetCollector`, `ViewCreator`, `ElementTag`, `Elevations`, the session and its ExternalEvent handler |
| `UI` | `WetAreaWaterproofingWindow`, `ViewModel_WetAreaWaterproofing` (+ `.Grid`, `.Run`, `.Views`, `.Assign`), `Controls/DraftingViewSearchBox` |

`Core` has no Revit references and is covered by the unit tests. Lengths in `Core` are millimetres.

## Assigning drafting views

`RoomCollector.ReadDraftingViews` lists every drafting view with its **Title on Sheet** (`VIEW_DESCRIPTION`) and,
from the viewports, its **sheet number**, **sheet name** and **bubble** (`VIEWPORT_DETAIL_NUMBER`). Two ways to use it:

- **Inline** — each "Points to" box (`DraftingViewSearchBox`) searches while you type and drops down the matches.
  The value reaches the settings only on a pick or when the box loses focus (as V001's combo box did).
- **Picker panel** — *Assign drafting views…* opens a card with the targets on the left and a searchable table on
  the right. Assigning moves on to the next target.

Search rules (`DraftingViewSearch`, unit-tested): words are ANDed; a word matches name, title, sheet number,
sheet name or bubble; `A-501/3` = sheet A-501, bubble 3; prefixes `s:` `d:` `n:` `t:`. Ranking: exact name, then
exact bubble / sheet, then names starting with the text, then by sheet and bubble (natural order).
A typed `A-501/3` that names one placed view is replaced by that view's name; any other text is kept and, if no
view has that name, a new drafting view is created at the run (V001 behaviour). Views are still saved by name.

## Section modes (2.1.0)

| Mode | Places on the room plan | Points to / makes |
|---|---|---|
| Ref. Section | Reference section marker (`CreateReferenceSection`) | The row's drafting view |
| Callout | Callout (`CreateCallout`), axis-aligned box = cut line + callout margin | A new detail view, `WP Callout - <target>` |
| Section | Section marker of a new view (`CreateSection`) | A new section view, `WP Section - <target>`: width = Length, crop from *below level* to *above level*, far clip = depth |
| Ref. Callout | Reference callout (`CreateReferenceCallout`), same box as Callout | The row's drafting view |

`SectionMode` values are stored in settings: append only (0 Ref. Section, 1 Callout, 2 Section, 3 Ref. Callout).
Before placing, `ViewCreator.RemoveEarlier` deletes the room's tagged markers and tagged callout / section views
(kind `Section`, same room key); a view that owns drawn elements is kept with a warning.

## How elements are tracked

Every roof gets `WP|<RunId>|<RoomKey>|<Number>|<Name>` in **Comments** (`RunId` = `yyyyMMdd-HHmmss`,
`RoomKey` = `Host:<room UniqueId>` or `<link instance UniqueId>:<room UniqueId>`). A later run finds the roof by
its room key and replaces it; *Remove a run* finds elements by run ID. The format is append-only.

Views have no Comments, so plans, section markers and tool-made drafting views carry the same data in
Extensible Storage: schema `WetAreaWaterproofing_Tag_V1` (`ElementTag`, fields `RunId`, `RoomKey`, `Kind`).
Never change that schema's GUID or fields; a new layout gets a new schema and a migration.
Reference sections are created with `ViewSection.CreateReferenceSection`, which returns nothing: the marker
is found by comparing the `OST_Viewers` ids before and after.

## Excel report

Sheets: Summary, Rooms, Boundary repair, Sections, Issues. Column names and order are fixed (`ReportWriter.RoomColumns`
and friends); new columns are added at the end only. `Group` and `Scale` are filled from part C on.

## Known limits

- Curved boundaries are faceted (the repair chain works on straight pieces).
- A slab is looked for within 500 mm above or below the room's level.
- Only roof types a footprint roof can be made from are offered: sloped glazing types are left out.
- Every elevation used in geometry is a Revit internal Z (`Elevations.InternalZFt`, i.e. `Level.Elevation`),
  never `ProjectElevation`: one base for level heights, slab tops, the search bands and the points handed back
  to Revit. The **Slab top** shown in the grid and written to the report is the only exception — it adds
  `RoomRow.ProjectBaseOffsetMm` (`Elevations.ProjectBaseOffsetFt`) so it reads as Revit's Properties panel does.
  That offset is display-only; never add it to a value that meets element geometry.
- Reference sections cannot be named one by one; they show the drafting view's detail number.
- *Remove a run* leaves drafting views in place.
- Needs testing in Revit on: a rotated link, a workshared model, rooms with columns inside.
