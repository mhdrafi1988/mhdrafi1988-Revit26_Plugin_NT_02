# Floor Divide Inner Loops — V001

Current version: **1.0.0** (`ToolCatalog.FloorInnerLoopDivider`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Divide Inner Loops V012** (`Menu/RoofTools/Points/InnerLoopDivider_V012`). Opening detection (sketch openings and opening cuts in the top faces), division planning and the window are the same.

## Wiring

- **Ribbon:** "Floor Points" panel ▸ **Inner Loops**, button `Btn_FloorInnerLoopDivider_V001`, built by `FloorToolsRibbon.BuildPointsPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorInnerLoopDivider.V001.Commands.InnerLoopDividerCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `RoofBase` | `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Sketch loops | `FootPrintRoof.GetProfiles()` | `Shared/Services/FloorSketchProfile.GetLoops` (`floor.SketchId`, else the dependent `Sketch`) |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler`; unexpected errors are logged by `ToolGuard` |

## Test checklist (Revit 2026)

- Floor with two circular openings and one opening cut: all three are listed; Apply adds the points, one Undo.
- "Reset existing points" off: an existing slope is kept.
- A floor in a group cannot be picked; a floor borrowed by another user is refused with the reason.
- The roof tool behaves exactly as before.
