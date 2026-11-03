# Floor Slope By Drain — V001

Current version: **1.0.0** (`ToolCatalog.FloorSlopeByDrain`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Auto Slope By Drain (Multi-Roof) V013**
(`Menu/RoofTools/RoofSlope/ByDrains/AutoSlopeByDrain_V013`). The slope engine is the same: Dijkstra over the
shape points, curve-intersection points, path samples, read-back, circle markers, Excel export and parameter
write-back. Only the element handling is floor-specific.

## Wiring

- **Ribbon:** the "Floor Slope" panel, button `Btn_FloorSlopeByDrain_V001` ("By Drain"), built by
  `FloorToolsRibbon.BuildSlopePanel` in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Commands.FloorSlopeByDrainCommand`
  (through `ToolGuard.RunCommand`; one window at a time).
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorSlopeByDrain\settings.v001.json` (has a `SettingsVersion`
  field).
- **Excel:** `Documents\FloorSlope_Reports` by default. Per-floor files are named `FloorSlopeByDrain_<id>_…`.
  The summary is `FloorSlopeByDrain_Summary_<date>.xlsx`.

## What is different from the roof tool

| Area | Roof V013 | Floor V001 |
|---|---|---|
| Elements | `FootPrintRoof` only | `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user. Floors that fail are logged and left out. |
| Type without a Variable layer | n/a | Continue / Skip dialog for each such floor (shape editing would tilt the whole slab) |
| Sketch | dependent `Sketch` search | `floor.SketchId`, falling back to the dependent search |
| Drains | sketch openings | sketch openings **plus drain families**: `OST_PlumbingFixtures` instances hosted on the floor or standing over its top face (within ±300 mm). They are shown in a "Fixture" group, and only names containing drain / gully / outlet / trap / sump start ticked. |
| Run | reset → match opening edges | reset → add a shape point at each ticked drain family (`FloorGeometryService.AddFixtureDrainPoints`) → match opening edges, or the fixture point within 10 mm |
| Level offset | `ROOF_CONSTRAINT_OFFSET_PARAM` | `FLOOR_HEIGHTABOVELEVEL_PARAM` |
| Parameters | `AutoSlope_*` on roofs | same names, written only where they are bound to Floors. When none is bound, the log says so. |

## Undo

There is one outer `TransactionGroup` per run ("Floor Slope By Drain"), with one group per floor inside it. A
floor that fails or is cancelled rolls back completely, including its reset and added drain points.

## Test checklist (Revit 2026)

1. Floor with a Variable-layer type, two circular openings and one floor-drain family: both kinds are listed,
   and the run slopes to all three.
2. Floor type without a Variable layer: the dialog appears. Skip leaves the floor unchanged and logs it.
3. A floor in a group, or borrowed by another user: it is logged as skipped and the other floors still open.
4. A WC on the same floor is listed under Fixture but not ticked.
5. Undo reverts the whole run in one step.
6. The roof By Drain tool behaves exactly as before.
