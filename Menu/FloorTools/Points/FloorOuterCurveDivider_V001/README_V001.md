# Floor Outer Curve Divider — V001

Current version: **1.0.0** (`ToolCatalog.FloorOuterCurveDivider`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Outer Curve Divider V006** (`Menu/RoofTools/Points/OuterCurveDivider_V006`). Curved-edge detection, division modes and the window are the same.

## Wiring

- **Ribbon:** "Floor Points" panel ▸ **Outer Curve**, button `Btn_FloorOuterCurveDivider_V001`, built by `FloorToolsRibbon.BuildPointsPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorOuterCurveDivider.V001.Commands.CurveDividerCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `RoofBase` | `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler`; unexpected errors are logged by `ToolGuard` |

## Test checklist (Revit 2026)

- Floor with an arc and a spline edge: both are listed and divided.
- A floor in a group cannot be picked; a floor borrowed by another user is refused with the reason.
- The roof tool behaves exactly as before.
