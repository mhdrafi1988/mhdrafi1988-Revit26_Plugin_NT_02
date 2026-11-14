# Floor Edge Element Sections — V001

Current version: **1.0.0** (`ToolCatalog.FloorEdgeElementSections`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Edge Element Sections V004** (`Menu/ViewTools/Create/RoofEdgeElementSections_V004`), the latest roof version. Linked-element search, section planning and naming are the same. It only reads the floors.

## Wiring

- **Ribbon:** Documents tab ▸ "Floor Views" ▸ **Floor Edge Element** (`Btn_FloorEdgeElementSections_V001`), built by `FloorToolsRibbon.BuildViewsPanel`.
- **Command:** `Revit26_Plugin_Revised.FloorEdgeElementSections.V001.FloorEdgeElementSectionsCommand` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorEdgeElementSections\settings.json`.

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Elements | `RoofBase` (Pick Roofs) | `Floor` (Pick Floors) |
| Settings folder | `RoofEdgeElementSections_V002` | `FloorEdgeElementSections` |
| Entry points | handler only through `ToolGuard` | command and handler through `ToolGuard` |

## Test checklist (Revit 2026)

- With a linked model loaded, pick a floor: sections are placed where linked elements meet its edges.
- The roof tool behaves exactly as before.
