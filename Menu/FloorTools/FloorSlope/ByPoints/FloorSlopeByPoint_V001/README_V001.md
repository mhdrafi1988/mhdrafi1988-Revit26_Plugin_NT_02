# Floor Slope By Point — V001

Current version: **1.0.0** (`ToolCatalog.FloorSlopeByPoint`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Auto Slope By Point V033**
(`Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033`). The solver (`Core/Services`: shortest paths, ridge
finder, clustering), window, circle markers, ridge lines, Excel export and parameter write-back are the same.
The Revit-facing code is in `Infrastructure/FloorOps` (renamed from `RoofOps`) and works on a `Floor`.

## Wiring

- **Ribbon:** the "Floor Slope" panel, button `Btn_FloorSlopeByPoint_V001` ("By Point"), built by
  `FloorToolsRibbon.BuildSlopePanel` in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorSlopeByPoint.V001.Commands.FloorSlopeByPointCommand`
  (through `ToolGuard.RunCommand`; one window at a time).
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorSlopeByPoint\settings.v001.json` (has a `SettingsVersion`
  field).
- **Excel:** `Documents\FloorSlope_Reports` by default (`FloorSlope_Results_<id>_<time>.xlsx` when saving).
- **Tests:** `Tests/FloorSlopeByPoint.Tests` (`dotnet test Tests/FloorSlopeByPoint.Tests`).

## What is different from the roof tool

| Area | Roof V033 | Floor V001 |
|---|---|---|
| Element | any `RoofBase` | one `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Type without a Variable layer | n/a | Continue / Cancel dialog before shape editing is turned on |
| Level offset | `ROOF_CONSTRAINT_OFFSET_PARAM` | `FLOOR_HEIGHTABOVELEVEL_PARAM` |
| Parameters | `AutoSlope_*` on roofs | same names, written only where they are bound to Floors. Each missing one is logged. |

## Test checklist (Revit 2026)

1. Floor with a Variable-layer type: pick 3 drain points, run with ridge lines on, then Undo.
2. Floor type without a Variable layer: the dialog appears, and Cancel leaves the model unchanged.
3. A floor in a group cannot be picked.
4. Excel export opens.
5. The roof By Point tool behaves exactly as before.
