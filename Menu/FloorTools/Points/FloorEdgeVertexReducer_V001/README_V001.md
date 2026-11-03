# Floor Edge Vertex Reducer — V001

Current version: **1.0.0** (`ToolCatalog.FloorEdgeVertexReducer`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Edge Vertex Reducer V009** (`Menu/RoofTools/Points/VertexReducer_V009`). Classification of points on straight edges, preview and removal are the same. The floor is picked from the window; the checks run when it is picked.

## Wiring

- **Ribbon:** "Floor Points" panel ▸ **Vertex Reducer**, button `Btn_FloorEdgeVertexReducer_V001`, built by `FloorToolsRibbon.BuildPointsPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorEdgeVertexReducer.V001.Commands.FloorEdgeVertexReducerCommand` (through `ToolGuard.RunCommand`). The ExternalEvent handler goes
  through `ToolGuard.RunHandler`.
- **Settings:** none saved (same as the roof tool).

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `FootPrintRoof` | `Floor`, host model, not in a group |
| Pre-checks | none | `Shared/Services/FloorShapeEditChecks`: shape-editing support, group, read-only, borrowed by another user |
| Sketch loops | `FootPrintRoof.GetProfiles()` | `Shared/Services/FloorSketchProfile.GetLoops` (`floor.SketchId`, else the dependent `Sketch`) |
| Entry points | plain `Execute` | `ToolGuard.RunCommand` / `ToolGuard.RunHandler`; unexpected errors are logged by `ToolGuard` |

## Test checklist (Revit 2026)

- Floor with extra points along a straight edge: Preview lists them, Apply removes them, one Undo.
- A floor in a group cannot be picked; a floor borrowed by another user is refused with the reason.
- The roof tool behaves exactly as before.
