# Floor Slope By Point (Ridge) — V001

Current version: **1.0.0** (`ToolCatalog.FloorSlopeByPointRidge`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Auto Slope By Point (Ridge) V003**
(`Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPointRidge_V003`). The engine (`Core/Engine`: Dijkstra paths, the
ridge rule in `RidgeEngine`, curve-intersection points, circle markers), the window, Excel export and parameter
write-back are the same. Only the element handling is floor-specific.

## Wiring

- **Ribbon:** "Floor Slope" panel ▸ **By Point** pulldown ▸ **Ridge**, button `Btn_FloorSlopeByPointRidge_V001`,
  built by `FloorToolsRibbon.BuildSlopePanel` in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorSlopeByPointRidge.V001.Commands.FloorSlopeByPointRidgeCommand`
  (through `ToolGuard.RunCommand`). The ExternalEvent handler goes through `ToolGuard.RunHandler`.
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorSlopeByPointRidge\settings.v001.json` (has a
  `SettingsVersion` field).
- **Excel:** `Documents\FloorSlope_Reports` by default (`FloorSlopeRidge_Results_<time>.xlsx` when saving).

## What is different from the roof tool

| Area | Roof Ridge V003 | Floor Ridge V001 |
|---|---|---|
| Element | any `RoofBase` | one `Floor`, host model, not in a group (`FloorSelectionFilter`) |
| Selection | always picks | uses a single selected floor, otherwise picks |
| Drain picks | any element, then checked | only points on the chosen floor can be picked (`PointOnFloorFilter`) |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Type without a Variable layer | n/a | Continue / Cancel dialog before shape editing is turned on |
| Shape editing | always calls `Enable()` | only when it is off; ESC during drain picks rolls it back |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler`; unexpected errors go to `ToolGuard.Report` |
| Level offset (Excel) | `ROOF_CONSTRAINT_OFFSET_PARAM` | `FLOOR_HEIGHTABOVELEVEL_PARAM` |
| Version written | hard-coded `R.01.00` | `ToolCatalog.FloorSlopeByPointRidge.Version` |
| Parameters | `AutoSlope_*` on roofs | same names, written where bound to Floors; missing ones are counted as skipped |

## Test checklist (Revit 2026)

1. Floor with a Variable-layer type: pick drains in two groups far apart, run with the ridge rule on. Ridge points
   are listed in the log and marked, then Undo.
2. Floor type without a Variable layer: the dialog appears, and Cancel leaves the model unchanged.
3. ESC while picking drains: shape editing is rolled back if it was off.
4. A floor in a group cannot be picked.
5. Excel export opens.
6. The roof Ridge tool behaves exactly as before.
