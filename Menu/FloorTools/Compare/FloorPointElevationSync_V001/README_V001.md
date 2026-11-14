# Floor Point Elevation Sync — V001

Current version: **1.0.0** (`ToolCatalog.FloorPointElevationSync`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Point Elevation Sync V005** (`Menu/RoofTools/Compare/CompareRoofs_V005`). Matching shape points by plan position, the match grid and applying the chosen heights are the same.

## Wiring

- **Ribbon:** "Floor Edit" panel ▸ **Elevation Sync**, button `Btn_FloorPointElevationSync_V001`, built by `FloorToolsRibbon.BuildEditPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorPointElevationSync.V001.Command` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorPointElevationSync\settings.json`.

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Elements | `RoofBase` A and B | `Floor` A and B, not in a group |
| Apply | no checks | `FloorShapeEditChecks` on both floors, and the Variable-layer Continue / Cancel dialog, before the transaction |
| Entry points | plain `Execute` | `ToolGuard` |

## Test checklist (Revit 2026)

- Two overlapping floors: Find matches lists the matched points; Apply copies the heights in one Undo.
- A floor type without a Variable layer: the dialog appears at Apply, and Cancel changes nothing.
- The roof tool behaves exactly as before.
