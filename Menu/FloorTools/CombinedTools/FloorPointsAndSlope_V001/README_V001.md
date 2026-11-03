# Floor Points & Slope — V001

Current version: **1.0.0** (`ToolCatalog.FloorPointsAndSlope`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Points & Slope V001** (`Menu/RoofTools/CombinedTools/RoofPointsAndSlope_V001`).
The pipeline, planners (`Core/Geometry`, `Core/Planning`, `Core/Engine/RidgePlanner`), window, presets and run report
are the same. Steps, in order:

1. Opening points, 2. perpendicular points, 3. mid/quarter points, 4. gap fill (`ShapePointWriter`);
5. ridge between the drain openings (floor copies of Ridge By Openings V070's services);
6. slope by drain (**Floor Slope By Drain V001**'s `AutoSlopeDrainEngine`).

## Wiring

- **Ribbon:** "Floor Slope" panel ▸ **Points & Slope**, button `Btn_FloorPointsAndSlope_V001`, built by
  `FloorToolsRibbon.BuildSlopePanel`.
- **Command:** `Revit26_Plugin_Revised.FloorPointsAndSlope.V001.Commands.FloorPointsAndSlopeCommand`
  (through `ToolGuard.RunCommand`; one window at a time). The handler goes through `ToolGuard.RunHandler`.
- **Settings / presets:** `%AppData%\Revit26_Plugin_Revised\FloorPointsAndSlope\settings.json` and `presets.json`
  (shared `SettingsService`, same format as the roof tool).

## What is different from the roof tool

| Area | Roof Points & Slope V001 | Floor Points & Slope V001 |
|---|---|---|
| Elements | `FootPrintRoof` | `Floor`, host model, not in a group |
| Sketch loops | `FootPrintRoof.GetProfiles()` | `FloorReader.ReadSketchProfile`: `floor.SketchId`, else the dependent `Sketch` |
| Pre-checks | group, borrowed | also `FloorShapeEditChecks` (shape editing, read-only) and, per floor, the Variable-layer Continue / Skip dialog |
| Drain detection | Auto Slope By Drain V013 (`DetectDrainsFromRoof`) | Floor Slope By Drain V001 (`DetectDrainsFromFloor`) |
| Slope engine | V013 `AutoSlopeDrainEngine` | Floor Slope By Drain V001 `AutoSlopeDrainEngine` |
| Ridge services | V070 `RoofBoundaryService`, `InnerLoopService`, `RidgeCreationService` | floor copies in `Infrastructure/Revit`: `FloorBoundaryService`, `FloorInnerLoopService`, `FloorRidgeCreationService` (V070's 2D helpers and models are still shared) |

## Known limit

Only **openings** in the floor sketch can be drains. Drain families (Plumbing Fixtures), which Floor Slope By Drain
lists in its Fixture group, are not listed here yet.

## Test checklist (Revit 2026)

1. Floor with a Variable-layer type and two circular openings: tick both as drains, run every step. Points are added,
   the ridge runs between the openings and the floor slopes to them. One Undo reverts it all.
2. Two floors picked, one type without a Variable layer: Skip leaves it out and the log says so.
3. A floor borrowed by another user is listed as skipped.
4. Floor with an opening cut (not in the sketch): it is found as "Opening cut".
5. The roof Points & Slope tool behaves exactly as before.
