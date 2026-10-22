# SloperPro

**SloperPro** is a Revit productivity add-in focused on building envelope workflows — roof slope automation, section/sheet management, workset organisation, and detail line tools.

## Compatibility

| SloperPro | Revit |
|---|---|
| 2.0.0 | 2026 |
| 1.0.0 | 2026 |

## Features

### Roof Tools
- **Auto Slope By Point / By Drain** — set roof slopes automatically from drain or reference points
- **Ridge Lines By Openings / By Points** — generate ridge lines from openings or point references
- **Creaser Advanced** — create crease lines on roofs
- **Inner/Outer Loop Divider, Line Intersect, Loops + Perpendicular** — divide and intersect roof loops
- **Vertex Reducer, Multi Points** — simplify roof geometry
- **Roof From Detail Lines** — generate roof elements from drawn detail lines
- **Elevation Sync, Roof Point Comparison** — sync and compare roof point elevations
- **Roof Tag** — tag roof elements automatically

### Floor Tools
- **Floors & Roof From Linked Rooms** — create floor/roof elements from rooms in linked models
- **Rooms Plan View** — generate plan views scoped to rooms

### View & Section Tools
- **Sections From Detail Lines / Place Sections** — create sections from detail lines or place them by layout
- **Edge Around Sections, Edge Element Sections** — add edge elements around section boxes
- **Callout To Section, Section Head Placer, Section View Tagger** — manage section heads and tags
- **Roof View Focus** — focus views to roof extents
- **Isolated Section** — one 3D view of the selected elements, section box turned parallel to their largest side, elements isolated
- **View Renamer, Section Renamer, Bubble Renumber** — batch rename views and renumber bubbles

### Sheet Tools
- **Smart View To Sheet Placer** — place views on sheets intelligently
- **Plan From Scope Box** — generate plans from scope boxes
- **Auto Section Placer** — automatically place sections on sheets
- **Sheet Auto Rearrange** — rearrange sheet layouts automatically

### Setup & Detail Lines
- **Batch Link DWG, DWG To Lines** — import DWG files and convert to detail lines
- **Linked Detail Line Generator** — generate detail lines from linked model elements
- **Detail Line Closed Loop** — close open detail line loops

### Manage
- **Workset Manager, Worksets From Links, Workset Renamer** — manage worksets and rename from Excel
- **ParaManager** — manage shared parameters
- **Annotation Overlap Detection** — detect overlapping annotations
- **Worksets & Elements Browser** — browse worksets and their element counts

### Memory
- **WS Memory** — remember which worksets are open as named sets, then reopen the model with exactly those worksets
- **Link Memory** — remember which Revit links are loaded as named sets, then load and unload links back to a set

## Installation

1. Download the latest release from the [Autodesk App Store](https://apps.autodesk.com).
2. Run the installer — it registers the add-in for Revit 2026 automatically.
3. Open Revit. A **SloperPro** tab appears in the ribbon.

## Version History

See [CHANGELOG.md](CHANGELOG.md) for the full history and per-tool versions.

### 2.0.0 — 2026-10-02
- One button per tool; superseded tool versions removed
- Every tool now reports errors instead of failing silently, and shows its version in the window title

### 1.0.0 — 2026
- Initial public release on Autodesk App Store
- 30+ tools across Roof, Floor, View, Sheet, Setup, and Manage panels

## License

© 2026 MuhammedRafi. All rights reserved.
