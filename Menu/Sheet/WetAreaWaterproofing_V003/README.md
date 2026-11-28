# Wet Area Waterproofing — V003

Finds wet rooms in the host model and its links, repairs their outlines and adds a **waterproofing roof on
top of the slab** under each one, then a cropped plan per room with the cuts the **Mapping** asks for.
Writes an Excel report of every run.

**V003 (3.0.0)** is V002 2.2.0 redesigned as four steps and with the Mapping (see *Mapping* below). It sits
first in the ribbon pulldown, above V002. It has its own settings file (`settings_v003.json`); the first time it
reads V002's `settings.json` and migrates it. It shares V002's Extensible Storage tag and report folder.

| Item | Value |
|---|---|
| Tool name | Wet Area Waterproofing, v3.0.0 (plugin 8.0.0) |
| Folder | `Menu/Sheet/WetAreaWaterproofing_V003/` |
| Namespace | `Revit26_Plugin_Revised.WetAreaWaterproofing.V003` |
| Command | `…V003.Commands.WetAreaWaterproofingCommand` (via `ToolGuard.RunCommand`) |
| Ribbon | **Waterproofing › Wet Area Waterproofing ▾ › Wet Area Waterproofing V003** (`Btn_WetAreaWaterproofing_V003`, first in `Pulldown_WetAreaWaterproofing`) |
| Catalog | `ToolCatalog.WetAreaWaterproofingV003` |
| Settings | `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\settings_v003.json` (`Version` 2; 1 = a V002 file) |
| Reports | `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\Reports\WP_Report_<Model>_<yyyyMMdd_HHmm>.xlsx` |
| Tests | `dotnet test Tests/WetAreaWaterproofingV003.Tests` |
| Preview | `Claude/Previews/WetAreaWaterproofing_V003_Window.html` |

## Steps (3.0.0)

A step bar shows the four steps; a passed step is clicked to change it (`ViewModel_WetAreaWaterproofing.Steps.cs`,
`CurrentStep` 1–4, `StepState1..4` = active / done / locked / pending).

1. **Find rooms** — the room grid; beside it, the ticked rooms whose outline did not close and the boundary repair.
2. **Roof** — roof type, workset, offset.
3. **Mapping** — *Categories to load*, the *Views* card (plan, section-view and callout settings, Assign drafting
   views…) and the Mapping table. *Run without review* is offered here.
4. **Review & run** — the Review table; after the run it is replaced by the result and the Excel report card.

## Mapping (3.0.0)

`Core/Mapping/MappingRule.cs`: a `MappingRule` is one row — `Id`, `On`, `Name`, `Category` (a Revit category name,
or `Room` for full-room cuts), `Match` (`Any` / `Keywords` / `Types`), `Keywords`, `TypeNames`, `Cut`
(`ThroughElement`, `LongestWall`, `RoomCorners`, `FullRoomH/V/HV`), `Mode` (`SectionMode`), `AllMatches`, `LengthMm`,
`DraftingViewName`. Enum values are stored: append only.

- `WetAreaSettings.Normalize` builds `Mappings` from a V002 file (`MappingRules.FromV002`) or the defaults, then
  derives `Targets` from it (`MappingRules.ToTargets`), so the V002 planner, Review and run keep working: each row
  is one `SectionTargetSetting` (two for H + V) carrying `RuleId`, `Label` and `WallTypeFilter`. A door row cut at
  the threshold keeps kind `Door` (doors may sit 400 mm outside the outline); other element rows use kind `Element`.
  Rows of a category not under *Categories to load* are off.
- `TargetCollector` reads only `MappingCategories`, only from the host (`MappingHost`) and the links not in
  `MappingLinksOff`. An element matched by several rows is listed once per row. Walls come with their location line
  and half width; a wall belongs to a room when the middle of a room edge is within half its width + 150 mm.
- `ListCategories` (for *+ Add category*) offers model categories with at least one placed element in the ticked
  models; the search shows matches only once something is typed. `LoadTypes` returns "Family : Type" × model × count.
- The room grid's Doors / Drains / Fixtures counts come from the Mapping's elements (drains = rows named "…drain…").

## Code layout

| Folder | Job |
|---|---|
| `Core/Mapping` | `MappingRule`, `MappingRules` (defaults, V002 migration, matching, `ToTargets`), `CategoryChoice`, `LoadedType` |
| `Core/Matching` | `KeywordMatcher` — is a room name a wet area |
| `Core/Geometry` | `LoopRepair` — the outline repair chain; `Polyline2D`, `Hull` |
| `Core/Grid` | `GridQuery`, `GridState` — filters, sort, level buttons |
| `Core/Run` | `RunPlanner` (what a run does per room), `RunTag` (Comments text), `RoomStatusRules` |
| `Core/Sections` | `DraftingViewSearch` / `DraftingViewInfo` (the "Points to" search), `SectionTargets` (the table), `ItemClassifier` (which Mapping rows an element belongs to) / `RoomItemMatcher` (which elements and walls belong to which room), `SectionPlanner` (which cuts, where, max 6), `ViewNames` |
| `Core/Review` | `ReviewBuilder`, `ReviewItem`, `ReviewKeys` — the Review table and the keys a run leaves out |
| `Core/Report` | `RunReport`, `ReportWriter` — the Excel workbook |
| `Infrastructure` | Revit: `RoomCollector`, `BoundaryReader`, `SlabIndex`, `RoofCreator`, `RunRemover`, `TargetCollector`, `ViewCreator`, `ElementTag`, `Elevations`, the session and its ExternalEvent handler |
| `UI` | `WetAreaWaterproofingWindow`, `ViewModel_WetAreaWaterproofing` (+ `.Grid`, `.Run`, `.Views`, `.Mapping`, `.Assign`, `.Steps`), `Controls/DraftingViewSearchBox`, `Converters/StepConverters` |

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

## Review (2.2.0, kept)

**Review** calls `IWetAreaSession.Preview`, which reads the ticked rooms again and runs the same planning as a run
(`RunPlanner.Plan`, `SectionPlanner.Plan`) without a transaction; `Core/Review/ReviewBuilder` turns that into rows and
`ViewCreator.ReadEarlier` adds what earlier runs left (markers, and callout / section views with their drawn-element
counts). Each row has a key from `ReviewKeys` (roof, plan, or section = room + target kind + element cut + label).
**Create** passes the unticked keys to `IWetAreaSession.Run(..., excluded, ...)`: an excluded roof is not made (the
old one stays), an excluded plan skips the room's views, an excluded section is dropped from the planned list.
**Run without review** passes no exclusions.

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
