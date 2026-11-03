# Floor Crease Lines By Openings — V001

Current version: **1.0.0** (`ToolCatalog.FloorCreaseLinesByOpenings`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Ridge By Openings V070** (`Menu/RoofTools/LinesAndPoints/RoofRidgeLines_ByOpenings_V070`). Opening analysis, drain grouping, the Voronoi ridge computation, validation and the shape point / split line / detail line creation are the same; the classes keep their roof-tool names with Roof → Floor.

## Wiring

- **Ribbon:** "Floor Lines" panel ▸ **Crease By Openings**, button `Btn_FloorCreaseLinesByOpenings_V001`, built by `FloorToolsRibbon.BuildLinesPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorCreaseLinesByOpenings.V001.Commands.FloorRidgeCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Window title / version:** from `ToolCatalog.FloorCreaseLinesByOpenings`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `RoofBase` | `Floor`, host model, not in a group (command pick and the window's Select Floor) |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks` at both picks: shape-editing support, group, read-only, borrowed by another user |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler` |
| Title / version | fixed text (`Ridge By Openings — V070`) | `ToolCatalog` |

## Test checklist (Revit 2026)

- Floor with three circular openings: tick them as drains; crease lines appear midway between them as split lines, and detail lines when ticked. One Undo.
- A floor in a group cannot be picked; a borrowed floor is refused with the reason.
- The roof tool behaves exactly as before.
