# Floor Point Comparison — V001

Current version: **1.0.0** (`ToolCatalog.FloorPointComparison`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Point Comparison V003** (`Menu/RoofTools/Compare/RoofPointComparison_V003`). Point extraction and the A / B comparison grid are the same. It only reads the floors.

## Wiring

- **Ribbon:** "Floor Edit" panel ▸ **Comparison**, button `Btn_FloorPointComparison_V001`, built by `FloorToolsRibbon.BuildEditPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorPointComparison.V001.Commands.FloorComparisonCommand` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** none saved.

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Elements | `RoofBase` A and B | `Floor` A and B |
| Entry points | plain `Execute` | `ToolGuard` |

## Test checklist (Revit 2026)

- Two copies of a floor, one re-sloped: the differences are listed per point.
- The roof tool behaves exactly as before.
