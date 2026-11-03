# Floor Tapered EPS 2D Pattern — V001

Current version: **1.0.0** (`ToolCatalog.FloorTaperedEpsPattern`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Tapered EPS 2D Pattern V006** (`Menu/RoofTools/Modelling/TaperedEpsPattern2D_V006`), the latest roof version. The pattern engine (`Core/Pattern`), the window and the 2D detail-pattern writer are the same. It only reads the floor.

## Wiring

- **Ribbon:** Modelling tab ▸ "Floor Edit" ▸ **Floor EPS** (`Btn_FloorTaperedEpsPattern_V001`), built by `FloorToolsRibbon.BuildEditPanel`.
- **Command:** `Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Commands.TaperedEpsPattern2DCommand` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `FootPrintRoof` (extrusion roofs refused) | any `Floor` whose sketch can be read |
| Outline | `FootPrintRoof.GetProfiles()` | `Shared/Services/FloorSketchProfile.GetLoops` |
| Visibility check | `OST_Roofs` in the view | `OST_Floors` in the view |
| Entry points | plain `Execute` | `ToolGuard` |

## Test checklist (Revit 2026)

- In a plan view, a rectangular floor with no openings: the EPS pattern is drawn as detail items.
- A floor with an opening: the tool says openings are not supported, as on roofs.
- The roof tool behaves exactly as before.
