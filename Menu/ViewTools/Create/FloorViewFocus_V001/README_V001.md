# Floor View Focus — V001

Current version: **1.0.0** (`ToolCatalog.FloorViewFocus`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof View Focus V004** (`Menu/ViewTools/Create/RoofViewFocus_V004`), the latest roof version. Cropping the plan view to the selected elements is the same. It only reads the floors.

## Wiring

- **Ribbon:** Documents tab ▸ "Floor Views" ▸ **Floor Focus** (`Btn_FloorViewFocus_V001`), built by `FloorToolsRibbon.BuildViewsPanel`.
- **Command:** `Revit26_Plugin_Revised.FloorViewFocus.V001.Commands.FloorViewFocusCommand` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorViewFocus\settings.json` (logs in `...\FloorViewFocus\Logs`).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Elements | `RoofBase` (type from `RoofType`) | `Floor` (type from `FloorType`) |
| Entry points | plain `Execute` | `ToolGuard` |

## Test checklist (Revit 2026)

- In a plan view, select two floors and run: the view is cropped to them.
- The roof tool behaves exactly as before.
