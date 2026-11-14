# Floor Multiple Points (1/2–1/4) — V001

Current version: **1.0.0** (`ToolCatalog.FloorMultiplePoints`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Multiple Points V003** (`Menu/RoofTools/Points/MultiplePoints_V003`). Edge selection, half / quarter points and the window are the same.

## Wiring

- **Ribbon:** "Floor Points" panel ▸ **Multi Points**, button `Btn_FloorMultiplePoints_V001`, built by `FloorToolsRibbon.BuildPointsPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorMultiplePoints.V001.Commands.MultiplePointsCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `RoofBase` | `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler`; unexpected errors are logged by `ToolGuard` |

## Test checklist (Revit 2026)

- Floor: tick half and quarter on two edges; six points are added.
- A floor in a group cannot be picked; a floor borrowed by another user is refused with the reason.
- The roof tool behaves exactly as before.
