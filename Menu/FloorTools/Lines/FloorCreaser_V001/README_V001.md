# Floor Creaser — V001

Current version: **1.0.0** (`ToolCatalog.FloorCreaser`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Creaser Adv V012** (`Menu/RoofTools/Lines/Creaser/CreaserAdv_V012`). Crease extraction from the top-face facets, the filters (horizontal, minimum length, Dijkstra validity) and detail-item placement are the same. It only reads the floor.

## Wiring

- **Ribbon:** "Floor Lines" panel ▸ **Floor Creaser**, button `Btn_FloorCreaser_V001`, built by `FloorToolsRibbon.BuildLinesPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorCreaser.V001.Commands.CreaserAdvCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Window title / version:** from `ToolCatalog.FloorCreaser`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Pick | category `OST_Roofs` | `elem is Floor` (grouped floors allowed: the floor is only read) |
| Entry points | `ToolGuard` (unchanged) | `ToolGuard` |

## Test checklist (Revit 2026)

- A floor sloped with Floor Slope By Drain: detail items are placed along its creases in a plan view.
- The roof tool behaves exactly as before.
