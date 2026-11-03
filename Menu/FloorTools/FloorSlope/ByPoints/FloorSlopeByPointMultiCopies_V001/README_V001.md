# Floor Slope By Point (Multi Copies) — V001

Current version: **1.0.0** (`ToolCatalog.FloorSlopeByPointMultiCopies`). Release notes are in the repo's
`CHANGELOG.md`.

This is the floor version of **Auto Slope By Point (Multi Copies) 30**
(`Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_MultiCopies30`). For each ticked slope row the floor is copied
in place (`ElementTransformUtils.CopyElement`, zero offset), the copy is sloped down to the picked drain points, and
it is put on the workset `Floor_Slope_<n>%` (created when missing). The engine, the window, Excel export and
parameter write-back are the same as the roof tool's. Only the element handling is floor-specific.

## Wiring

- **Ribbon:** "Floor Slope" panel ▸ **By Point** pulldown ▸ **Multi Copies**, button
  `Btn_FloorSlopeByPointMultiCopies_V001`, built by `FloorToolsRibbon.BuildSlopePanel`.
- **Command:** `Revit26_Plugin_Revised.FloorSlopeByPointMultiCopies.V001.Commands.FloorSlopeByPointMultiCopiesCommand`
  (through `ToolGuard.RunCommand`). The ExternalEvent handler goes through `ToolGuard.RunHandler`.
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorSlopeByPointMultiCopies\settings.v001.json` (has a
  `SettingsVersion` field).
- **Excel:** `Documents\FloorSlope_Reports` by default (`FloorSlopeMultiCopies_Results_<time>.xlsx` when saving).
- **Needs:** a workshared model (as on roofs).

## What is different from the roof tool

| Area | Roof Multi Copies 30 | Floor Multi Copies V001 |
|---|---|---|
| Element | any `RoofBase` | one `Floor`, host model, not in a group |
| Selection / drain picks | always picks; picks checked afterwards | single selected floor used; only points on that floor can be picked |
| Pre-checks | none | `FloorShapeEditChecks` (shape editing, group, read-only, borrowed) and the Variable-layer Continue / Cancel dialog |
| Workset name | `Roof_Slope_<n>%` | `Floor_Slope_<n>%` |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler` |
| Level offset (Excel) | `ROOF_CONSTRAINT_OFFSET_PARAM` | `FLOOR_HEIGHTABOVELEVEL_PARAM` |
| Version written | hard-coded `MCOP28.01.00` | `ToolCatalog.FloorSlopeByPointMultiCopies.Version` |

## Test checklist (Revit 2026)

1. Workshared model, floor with a Variable-layer type: tick 1 % and 2 %, run. Two copies appear on
   `Floor_Slope_1%` and `Floor_Slope_2%`, each sloped, and one Undo removes both.
2. Non-workshared model: the run stops with "requires worksets" and nothing changes.
3. Floor type without a Variable layer: the dialog appears, and Cancel leaves the model unchanged.
4. The roof Multi Copies tool behaves exactly as before.
