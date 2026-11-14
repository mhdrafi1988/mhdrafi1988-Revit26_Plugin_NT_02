# Roof Slope Builder V001 (tool 1.1.0)

One command that adds shape-edit points and applies slope-by-drain to every selected footprint roof.

Ribbon: Roof Slope panel → Points & Slope pulldown → **Roof Slope Builder**
(`Revit26_Plugin_Revised.RoofSlopeBuilder.V001.Commands.RoofSlopeBuilderCommand`).

## Folders
- `Commands\` – the ribbon command (runs through `ToolGuard.RunCommand`).
- `Core\` – no Revit references (geometry, Voronoi ridges, height rule, settings). Lengths in feet.
  Unit tests: `dotnet test Tests/RoofSlopeBuilder.Tests`.
- `Infrastructure\` – Revit I/O: `RoofReader` (sketch → plan geometry), `RoofWriter` (`SlabShapeEditor`),
  `RoofChecks` (pre-run checks), `SettingsStore` (JSON under `%AppData%\Revit26_Plugin_Revised\RoofSlopeBuilder`).
- `UI\` – the options window.

## How it works
- The roof sketch is read: largest loop = outline, other loops = openings.
- Drains = openings up to the max size; by default only the smallest size class.
- Height of every shape point = slope × distance to the nearest drain's outline. Drain outlines stay at 0.
- Ridges = Voronoi cell borders between drain centres, cut to the roof outline and openings.
- Saddle mode: drains sit on a valley line; a raised diamond (cricket) is built between neighbouring drains.

## Not yet
Manual drain picking, preview in the model, presets, CSV log, weighted ridges for drains of different sizes,
and distance measured around re-entrant corners on L/U-shaped roofs (straight-line distance is used).
