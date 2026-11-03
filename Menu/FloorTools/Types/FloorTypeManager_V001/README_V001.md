# Floor Type Manager — V001

Current version: **1.0.0** (`ToolCatalog.FloorTypeManager`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Type Manager V003** (`Menu/RoofTools/RoofTypeManager/RoofTypeCreator_V003`). Type listing, Excel export and creating types from the Excel template (layers, Variable layer, wraps, layer priorities) are the same.

## Wiring

- **Ribbon:** "Floor Edit" panel ▸ **Floor Types**, button `Btn_FloorTypeManager_V001`, built by `FloorToolsRibbon.BuildEditPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorTypeManager.V001.Commands.FloorTypeManagerCommand` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** none saved; Excel files are chosen in the window.

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Types | `RoofType` | `FloorType` (duplicated from a template floor type) |
| Excel names | `RoofTypes_Export_…`, `RoofType_Import_Template` | `FloorTypes_Export_…`, `FloorType_Import_Template` |
| Entry points | plain `Execute` | `ToolGuard` |

## Test checklist (Revit 2026)

- Export the floor types to Excel, add two rows to the template, import: two floor types are created, one with a Variable layer.
- The roof tool behaves exactly as before.
