# Floor Detail Line Intersect — V001

Current version: **1.0.0** (`ToolCatalog.FloorDetailLineIntersect`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Detail Line Intersect V014** (`Menu/RoofTools/Points/RoofDetailLineIntersect_V014`). Intersection, nudge and point-adding logic and the window are the same.

## Wiring

- **Ribbon:** "Floor Points" panel ▸ **Line Intersect**, button `Btn_FloorDetailLineIntersect_V001`, built by `FloorToolsRibbon.BuildPointsPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorDetailLineIntersect.V001.Commands.FloorDetailLineIntersectCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `FootPrintRoof` | `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Sketch loops | `FootPrintRoof.GetProfiles()` | `Shared/Services/FloorSketchProfile.GetLoops` (`floor.SketchId`, else the dependent `Sketch`) |; loops are sorted largest plan area first, so the outer boundary is always first |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler`; unexpected errors are logged by `ToolGuard` |

## Test checklist (Revit 2026)

- In a floor plan, a floor with an opening and three detail lines crossing it: points appear at every crossing.
- A floor in a group cannot be picked; a floor borrowed by another user is refused with the reason.
- The roof tool behaves exactly as before.
