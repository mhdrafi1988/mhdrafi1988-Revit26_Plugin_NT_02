# Floor Name Builder — V001

Current version: **1.0.0** (`ToolCatalog.FloorNameBuilder`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Name Builder V002** (`Menu/RoofTools/RoofNameBuilder_V002`). The INS / SLP / MAT code builder, types and instances modes, scopes and duplicate checks are the same.

## Wiring

- **Ribbon:** "Floor Edit" panel ▸ **Name Builder**, button `Btn_FloorNameBuilder_V001`, built by `FloorToolsRibbon.BuildEditPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorNameBuilder.V001.Commands.FloorNameBuilderCommand` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorNameBuilder\settings.json`.

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Elements | `RoofType`, `RoofBase` by `OST_Roofs` | `FloorType`, `Floor` by `OST_Floors` |
| Entry points | `ToolGuard` | `ToolGuard` (unchanged) |

## Test checklist (Revit 2026)

- Types mode: rename two floor types; every floor of those types shows the new name. One Undo.
- The roof tool behaves exactly as before.
