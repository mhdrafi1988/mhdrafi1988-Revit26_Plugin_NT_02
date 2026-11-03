# Floor Inner Loops And Perpendicular — V001

Current version: **1.0.0** (`ToolCatalog.FloorInnerLoopsAndPerpendicular`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Inner Loops And Perpendicular V007** (`Menu/RoofTools/Points/InnerLoopsAndPerpendicular_V007`). Loop detection, perpendicular points and the window are the same. **As on roofs, it resets every shape point to 0 when it opens**, so run it before sloping.

## Wiring

- **Ribbon:** "Floor Points" panel ▸ **Loops + Perp.**, button `Btn_FloorInnerLoopsAndPerpendicular_V001`, built by `FloorToolsRibbon.BuildPointsPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorInnerLoopsAndPerpendicular.V001.Commands.InnerLoopsAndPerpendicularCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `RoofBase` | `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Pick | any element, then checked | `Shared/Services/FloorPickFilter` |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler`; unexpected errors are logged by `ToolGuard` |

## Test checklist (Revit 2026)

- Floor with two circular openings: points around them and perpendicular to the boundary are added.
- A floor in a group cannot be picked; a floor borrowed by another user is refused with the reason.
- The roof tool behaves exactly as before.
