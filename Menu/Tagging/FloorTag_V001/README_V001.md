# Floor Tag — V001

Current version: **1.0.0** (`ToolCatalog.FloorTag`). Release notes are in the repo's `CHANGELOG.md`.

This is the floor version of **Roof Tag V018** (`Menu/Tagging/RoofTag_V018`). Spot elevations on the top face (face references), clustering, loop reduction and duplicate checks are the same.

## Wiring

- **Ribbon:** "Floor Edit" panel ▸ **Floor Tag**, button `Btn_FloorTag_V001`, built by `FloorToolsRibbon.BuildEditPanel`
  in `Menu/00_Push_Button_Menu_items/Ribbon/FloorRibbon.cs`.
- **Command:** `Revit26_Plugin_Revised.FloorTag.V001.FloorTagCommand` (through `ToolGuard.RunCommand`); handlers go through `ToolGuard.RunHandler`.
- **Settings:** none saved; the log is written to `Documents\FloorTag_V001_Log.txt`.

## What is different from the roof tool

| Area | Roof tool | Floor V001 |
|---|---|---|
| Element | `RoofBase`; loop reduction needs a `FootPrintRoof` | `Floor`, host model, not in a group |
| Sketch loops | `FootPrintRoof.GetProfiles()` | `Shared/Services/FloorSketchProfile.GetLoops` |
| Pre-checks | none | `FloorShapeEditChecks` before shape editing is turned on |
| Entry points | try / catch in `Execute` | `ToolGuard.RunCommand` |

## Test checklist (Revit 2026)

- Floor sloped with Floor Slope By Drain: spot elevations appear at its shape points in a plan view; running again adds no duplicates.
- The roof tool behaves exactly as before.
