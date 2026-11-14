# Floor Crease Lines By Points — V001

Current version: **1.0.0** (`ToolCatalog.FloorCreaseLinesByPoints`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Ridge By Points V059** (`Menu/RoofTools/LinesAndPoints/RoofRidgeLines_ByPoints_V059`). Drain point picking, grouping, the Voronoi ridge computation and the Reset → Add Shape Points → Reset → Detail Lines pipeline are the same.

## Wiring

- **Ribbon:** "Floor Lines" panel ▸ **Crease By Points**, button `Btn_FloorCreaseLinesByPoints_V001`, built by `FloorToolsRibbon.BuildLinesPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorCreaseLinesByPoints.V001.Commands.FloorRidgeCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Window title / version:** from `ToolCatalog.FloorCreaseLinesByPoints`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `RoofBase` | `Floor`, host model, not in a group (command pick and the window's Select Floor) |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks` at both picks: shape-editing support, group, read-only, borrowed by another user |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler` |
| Title / version | fixed text (`Floor Ridge — Voronoi Generation — V059`) | `ToolCatalog` |

## Test checklist (Revit 2026)

- Floor: click four drain points in two pairs; the crease line between the pairs is created. One Undo.
- A floor in a group cannot be picked; a borrowed floor is refused with the reason.
- The roof tool behaves exactly as before.
