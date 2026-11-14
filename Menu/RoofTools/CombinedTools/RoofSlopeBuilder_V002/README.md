# Roof Slope Builder V002 (tool 2.0.0)

One command that adds shape-edit points and applies slope-by-drain to every selected footprint roof.

Ribbon: Roof Slope panel → **Roof Slope Builder** dropdown → Roof Slope Builder V002 (first item; icon
`Resources/Icons/RoofTools/RoofSlopeBuilder_V002_16/32.png`). V001 stays in the same dropdown below it.
(`Revit26_Plugin_Revised.RoofSlopeBuilder.V002.Commands.RoofSlopeBuilderCommand`).

## Folders
- `Commands\` – the ribbon command (runs through `ToolGuard.RunCommand`).
- `Core\` – no Revit references (geometry, Voronoi ridges, height rule, settings). Lengths in feet.
  Unit tests: `dotnet test Tests/RoofSlopeBuilderV002.Tests`.
- `Infrastructure\` – Revit I/O: `RoofReader` (sketch → plan geometry), `RoofWriter` (`SlabShapeEditor`),
  `RoofChecks` (pre-run checks), `RidgeDrawer` (ridge lines and points in the plan view),
  `SettingsStore` (`settings_v002.json` under `%AppData%\Revit26_Plugin_Revised\RoofSlopeBuilder`; the V001
  `settings.json` is migrated on first run and left in place).
- `UI\` – the options window.

## How it works
- The roof sketch is read: largest loop = outline, other loops = openings.
- Drains = openings up to the max size; by default only the smallest size class.
- Height of every shape point = slope × distance to the nearest drain's outline. Drain outlines stay at 0.
- Ridges = Voronoi cell borders between drain centres, cut to the roof outline and openings.
- Saddle mode: drains sit on a valley line; a raised diamond (cricket) is built between neighbouring drains.

## New in V002: ridge drawing (tab 5 - Ridges)
- **Ridge lines**: watershed mode, the lines halfway between drains (Voronoi borders, joined end to end);
  saddle mode, each saddle's crest (side tip - peak - side tip). Valley lines are not drawn.
- Drawn as **detail lines** in a chosen line style, or as a **line-based detail family** (one instance per
  straight piece). Fallbacks, all counted in the report:
  family missing / not activatable → all ridges as detail lines; one placement refused → that piece as a
  detail line; line style missing or refused → Thin Lines → Revit's default; piece too short → skipped.
- **Ridge points**: line ends, junctions and points along the ridges at the ridge point spacing, marked with
  detail circles (two half arcs each) in the same line style.
- Drawn in the active **plan view** only, at its level's elevation. With Apply they go in each roof's
  transaction (one Undo for slope and drawing); **Find ridges only** draws them and leaves the roofs alone.

## Not yet
Manual drain picking, preview in the model, presets, CSV log, weighted ridges for drains of different sizes,
and distance measured around re-entrant corners on L/U-shaped roofs (straight-line distance is used).
