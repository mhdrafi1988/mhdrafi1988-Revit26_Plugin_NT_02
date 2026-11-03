# Floor Edge Around Sections — V001

Current version: **1.0.0** (`ToolCatalog.FloorEdgeAroundSections`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Edge Around Sections V007** (`Menu/ViewTools/Create/RoofEdgeAroundSections_V007`), the latest roof version. Edge bucketing, section planning and naming are the same. It only reads the floors.

## Wiring

- **Ribbon:** Documents tab ▸ "Floor Views" ▸ **Floor Around Edges** (`Btn_FloorEdgeAroundSections_V001`), built by `FloorToolsRibbon.BuildViewsPanel`.
- **Command:** `Revit26_Plugin_Revised.FloorEdgeAroundSections.V001.FloorEdgeAroundSectionsCommand` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** `%AppData%\Revit26_Plugin_Revised\FloorEdgeAroundSections\settings.json`.

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Elements | `RoofBase` in the selection | `Floor` in the selection (other elements skipped) |
| Settings folder | `RoofEdgeSections_V005` | `FloorEdgeAroundSections` |
| Entry points | handler only through `ToolGuard` | command and handler through `ToolGuard` |

## Test checklist (Revit 2026)

- Select a floor with a curved edge: sections are created around every edge.
- The roof tool behaves exactly as before.
