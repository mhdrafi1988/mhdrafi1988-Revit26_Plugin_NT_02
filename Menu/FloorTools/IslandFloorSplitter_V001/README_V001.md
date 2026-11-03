# Island Floor Splitter — V001

Current version: **1.0.0** (`ToolCatalog.IslandFloorSplitter`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Island Roof Splitter V002** (`Menu/RoofTools/IslandRoofSplitter_V002`), the latest
roof version. Island finding (planar graph, crossings, nesting), the preview, the per-island ladder of attempts,
creation verification and the shape-point push are the same.

## Wiring

- **Ribbon:** Modelling tab ▸ "Floor Edit" ▸ **Island Splitter** (`Btn_IslandFloorSplitter_V001`, large button),
  built by `FloorToolsRibbon.BuildEditPanel`.
- **Command:** `Revit26_Plugin_Revised.IslandFloorSplitter.V001.Commands.IslandFloorSplitterCommand` (through
  `ToolGuard.RunCommand`; the handler too).
- **Settings:** `%AppData%\Revit26_Plugin_Revised\IslandFloorSplitter\settings.json`.

## How it was ported

Island Roof Splitter V002 already has a floor output (`IslandFloorFactory`), so the blanket Roof → Floor rename used
for the other floor tools would have collided with it. This tool was ported by hand instead:

| Area | Roof tool V002 | Floor V001 |
|---|---|---|
| Source | one `FootPrintRoof` | one `Floor` (only read) |
| Source reading | `GetProfiles()` + slope-defining edges, `ROOF_LEVEL_OFFSET_PARAM` | shape-point count, `FLOOR_HEIGHTABOVELEVEL_PARAM` |
| Top faces | `RoofSurfaceExtractor.Extract(RoofBase)` | `Extract(HostObject)` (same code) |
| Output | Roof only / Roof, floor if it fails / Floor only | always **Floor**; the output chips and roof-type picker are hidden |
| Floor type | document default | the source floor's type, else the document default |
| Mark prefix | `R<sourceRoofId>_` | `F<sourceFloorId>_` |

Internal names (`RoofId`, `RoofSurfaceMesh`, `PickRoof`, `IslandRoofFactory`) are kept from the roof tool; in this
tool they refer to the source floor, and `IslandRoofFactory` is never called.

## Test checklist (Revit 2026)

1. A floor with two closed detail-line rectangles drawn over it in a plan view: Preview lists two islands; Commit
   creates two floors with marks `F<id>_Island_01/02`. One Undo removes both.
2. Sloped mode on a floor sloped with Floor Slope By Drain: the new floors follow the source floor's surface.
3. The source floor is unchanged.
4. The roof Island Splitter behaves exactly as before.
