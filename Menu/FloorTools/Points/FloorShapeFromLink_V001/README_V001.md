# Floor Shape From Link — V001

Current version: **1.0.0** (`ToolCatalog.FloorShapeFromLink`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Shape From Link V002** (`Menu/RoofTools/Points/RoofShapeFromLink_V002`). Link reading (surface or nearest point), the grid, apply and read-back are the same; the linked elements read are **floors** (`OST_Floors`).

## Wiring

- **Ribbon:** "Floor Points" panel ▸ **Shape From Link**, button `Btn_FloorShapeFromLink_V001`, built by `FloorToolsRibbon.BuildPointsPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorShapeFromLink.V001.Commands.FloorShapeFromLinkCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `FootPrintRoof` | `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Base level / offset | `ROOF_BASE_LEVEL_PARAM`, `ROOF_LEVEL_OFFSET_PARAM` | `LEVEL_PARAM`, `FLOOR_HEIGHTABOVELEVEL_PARAM` (a floor's level + offset is its **top**) |
| Thickness fallback | `ROOF_ATTR_DEFAULT_THICKNESS_PARAM` | `FLOOR_ATTR_DEFAULT_THICKNESS_PARAM` |
| Subtract thickness | on by default | **off** by default (the host top follows the linked top) |
| Apply | checkout check | also `FloorShapeEditChecks` and the Variable-layer Continue / Skip dialog per floor |
| Entry points | already `ToolGuard` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler`; unexpected errors are logged by `ToolGuard` |

## Test checklist (Revit 2026)

- Host floor under a linked floor that slopes 1 %: with Subtract off, the host top matches the linked top within read-back tolerance.
- Floor type without a Variable layer: the dialog appears at Apply; Skip leaves it unchanged.
- A floor in a group cannot be picked; a floor borrowed by another user is refused with the reason.
- The roof tool behaves exactly as before.
