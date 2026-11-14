# SloperPro

**SloperPro** is a Revit productivity add-in focused on building envelope workflows: roof and floor slope automation,
section/sheet management, workset organisation, and detail line tools.

## Compatibility

| SloperPro | Revit |
|---|---|
| 6.x (current: 6.37.0) | 2026 |
| 2.0.0 | 2026 |
| 1.0.0 | 2026 |

## Ribbon

SloperPro adds four ribbon tabs (two for model tools, two for sheet tools). Panels are listed left to right, in the order `App.cs` builds them.
A **▸ slide-out** is the arrow on a panel's title bar; the tools listed after it sit in the slide-out.
Where a tool has several versions, its button is a dropdown listing the newest version first.

**Rafi_Pro_MOds** and **Rafi_Pro_MOds2** (Modelling)

| Tab | Panel | Tools |
|---|---|---|
| Rafi_Pro_MOds | Roof Slope | By Point · By Drain · Points & Slope · Roof Slope Builder |
| Rafi_Pro_MOds | Floor Slope | By Drain · By Point (By Point, Ridge, Multi Copies) · Points & Slope |
| Rafi_Pro_MOds | Shape Points | Inner Loops · Outer Curve · Line Intersect · Loops + Perp. ▸ slide-out: Vertex Reducer · Multi Points · Shape From Link |
| Rafi_Pro_MOds | Floor Points | Inner Loops · Outer Curve · Line Intersect · Loops + Perp. · Vertex Reducer · Multi Points · Shape From Link |
| Rafi_Pro_MOds | Floor Lines | Crease By Openings · Crease By Points · Floor Creaser |
| Rafi_Pro_MOds | Floor Edit | Floor Types · Name Builder · Floor Tag · Comparison · Elevation Sync · Floor EPS |
| Rafi_Pro_MOds | Roof Tools | Creaser Adv · Roof Tag · Floor From Rooms · Shape From Link |
| Rafi_Pro_MOds2 | Roof Edit | Name Builder · Island Splitter · Merge Roofs |
| Rafi_Pro_MOds2 | Roof Plan View | Island Splitter |
| Rafi_Pro_MOds2 | Roof Compare | Comparison · Elevation Sync |
| Rafi_Pro_MOds2 | Roof Insulation | EPS 2D Pattern |
| Rafi_Pro_MOds2 | Detail Lines | From Links · Closed Loop · Line Dims · Line Manager |
| Rafi_Pro_MOds2 | Roof Utilities | Combined Tools · Ridge Lines · Roof Types |

**Rafi_Pro_Docs** and **Rafi_Pro_Docs2** (Documents)

| Tab | Panel | Tools |
|---|---|---|
| Rafi_Pro_Docs | View Create | From Lines · Around Edges · Edge Element ▸ slide-out: Roof Focus · Isolated Section |
| Rafi_Pro_Docs | Floor Views | Floor Around Edges · Floor Edge Element · Floor Focus |
| Rafi_Pro_Docs | View Place | Place Sections · Callout To Section · Head Placer · View Tagger |
| Rafi_Pro_Docs | View Rename | Bubble Renumber *button* · Section Renamer *button* · Auto Renamer *button* |
| Rafi_Pro_Docs2 | Tagging | Tag To Text · Align Spot Elevations · Align to Detail Line · Distribute Spot Elev. · Quick Align split buttons: Vertical · Horizontal · to Tag (each lists tags, ends, bends) · Quick Distribute split buttons: Vertical · Horizontal (each lists tags, ends, bends) |
| Rafi_Pro_Docs | Sheet Create | Scope Box · Sheet Placer · Section Placer · Sections To Sheet · Combine Sheets ▸ slide-out: Rearrange (V030, V029, V028) · Arrange Views |
| Rafi_Pro_Docs | Sheet Edit | Text Replace |
| Rafi_Pro_Docs2 | DWG Tools | DWG To DL(P) · Export DWG · DWG Manager · DWG Statistics |
| Rafi_Pro_Docs2 | Family Tools | Batch Link DWG · DWG To Lines |
| Rafi_Pro_Docs2 | Manage | Overlaps (V006, V005) · ParaManager · Shared Params · Schedule I/O · Link Check ▸ slide-out: Del. Line Styles · Bulk Rename · Tracker ID |
| Rafi_Pro_Docs2 | Worksets | WS Browser · WS Creator · WS Renamer ▸ slide-out: WS Renamer XL · Delete Workset |
| Rafi_Pro_Docs2 | Memory | WS Memory · Link Memory |
| Rafi_Pro_Docs2 | Coordination | Void Openings · Base Level to Parameter |

## Features

### Roof slope
- **Auto Slope By Point**: slope a roof's shape points down to drain points you pick. V033 adds optional ridge
  lines between drain groups; V034 lets you set the on-roof path check spacing and projection tolerance. The dropdown also has the Ridge and Multi Copies variants.
- **Auto Slope By Drain (Multi-Roof)**: pick one or more roofs and slope each one down to the drain openings in its
  sketch, with circle markers, Excel export and `AutoSlope_*` parameters.
- **Roof Points & Slope**: shape points around openings, perpendicular to the boundary, at mid/quarter points and in
  long gaps, the ridges between drains, then the slope, in one window and one Undo.
- **Roof Slope Builder** (new in 7.3.0, own button on the Roof Slope panel): one command for every selected footprint
  roof. Adds opening-corner, perpendicular, ridge and curve points, then sets each point to slope x distance to its
  nearest drain. Ridges between drains are watershed lines or saddles (diamond crickets). Options are remembered.

### Floor slope (new in 6.31.0)
- **Floor Slope By Drain**: the floor version of Auto Slope By Drain. Drains are openings in the floor sketch and
  drain families (Plumbing Fixtures) on the floor.
- **Floor Slope By Point**: the floor version of Auto Slope By Point, with optional ridge lines.
- **Floor Slope By Point (Ridge)** (new in 6.33.0): the floor version of Auto Slope By Point (Ridge). Lifts the
  ridge points between drain groups so every point drains at the given slope.
- **Floor Slope By Point (Multi Copies)** (new in 6.33.0): the floor version of Auto Slope By Point (Multi Copies).
  One sloped copy of the floor per slope, each on its own workset.
- **Floor Points & Slope** (new in 6.33.0): the floor version of Roof Points & Slope. Shape points around
  openings, ridges between drain openings, then slope by drain, in one window and one Undo.
- All of them refuse floors that are in a group, borrowed by another user or can't be shape-edited. They ask before sloping
  a floor whose type has no Variable layer, because the whole slab would tilt.

### Floor shape points (new in 6.34.0)
- The **Floor Points** panel has floor versions of every Shape Points tool: Inner Loops, Outer Curve, Line
  Intersect, Loops + Perpendicular, Vertex Reducer, Multi Points and Shape From Link. Same windows and options as
  the roof tools; they refuse floors that are in a group, borrowed by another user or can't be shape-edited.
- **Floor Shape From Link** sets each shape point from the linked floors' top surface. Because a floor's level +
  offset is its top, "Subtract host floor thickness" is off by default.

### Floor crease lines (new in 6.35.0)
- **Floor Crease Lines By Openings / By Points**: the floor versions of Ridge By Openings and Ridge By Points.
  They draw the crease (ridge) lines midway between the drains, as shape points + split lines and/or detail lines.
- **Floor Creaser**: the floor version of Creaser Adv. It places detail items along a sloped floor's creases and
  only reads the floor.

### Floor types, names, tags and checks (new in 6.36.0)
- **Floor Type Manager, Floor Name Builder**: export and create floor types from Excel; bulk-rename floor types with
  INS / SLP / MAT codes.
- **Floor Tag**: spot elevations on a floor's top face at its shape points.
- **Floor Point Comparison, Floor Point Elevation Sync**: compare two floors' shape-point heights, and copy heights
  between matching points (asks before changing a floor type with no Variable layer).

### Floor views and insulation (new in 6.37.0)
- **Floor Views** panel (Documents tab): **Floor Edge Around Sections**, **Floor Edge Element Sections** and
  **Floor View Focus**, the floor versions of the roof section and view-focus tools.
- **Floor EPS** (Floor Edit panel): the floor version of Tapered EPS 2D Pattern. It draws the tapered insulation
  layout as a 2D detail pattern for a floor with a single outer loop.

### Roof shape points, editing and checks
- **Inner Loops, Outer Curve, Line Intersect, Loops + Perpendicular**: divide and intersect roof loops into shape points.
- **Vertex Reducer, Multi Points (1/2–1/4)**: simplify roof edges and add mid/quarter points.
- **Roof Shape From Link**: set each shape point to the linked roof's top-surface elevation.
- **Creaser Adv, Roof Tag**: crease lines on roofs, and roof tagging.
- **Roof Name Builder, Island Roof Splitter**: bulk-rename roof types and instances with INS/SLP/MAT codes, and
  split a sloped roof into one roof per closed detail-line island.
- **Merge Roofs** (Roof Edit panel): select several footprint roofs and create one new roof from their combined outline
  (outer boundary and interior loops). It checks for overlap, crossing edges and gaps first; the original roofs are kept.
- **Roof Point Comparison, Elevation Sync**: compare and sync roof point elevations.
- **Tapered EPS 2D Pattern**: draw the tapered EPS board layout as detail lines on a plan view.
- **Ridge By Openings / By Points, Combined Roof Tools, Roof Type Manager** (Roof Utilities panel).

### Floors from rooms
- **Floors And Roof From Linked Rooms** (Roof Tools ▸ Floor From Rooms): create floors or a roof from rooms in linked
  models, directly or through a plan view.

### Views and sections
- **Sections From Detail Lines, Section Around Edges, Edge Element Sections**: create sections from detail lines and
  roof edges.
- **Roof View Focus, Isolated Section**: focus views on roof extents; make one 3D view of the selected elements with a
  section box turned parallel to their largest side.
- **Place Sections, Callout To Section, Section Head Placer, Section View Tagger**: place and tag section views.
- **Bubble Renumber, Section Renamer, View Auto Renamer**: batch renumber and rename.

### Tagging
- **Tag To Text**: copy spot elevations, coordinates, slopes and tags into text notes with leaders.
- **Align Spot Elevations**: line spot elevation tags up in a column or row, onto detail lines, or with the end or
  bend point of a tag you click. The Quick Align buttons do it in one click with the last-used settings.
  **Distribute Spot Elev.** spaces them evenly or at a fixed gap along their column or row; the Quick Distribute
  buttons do that in one click.

### Sheets
- **Plan From Scope Box, Smart View To Sheet Placer, Automated Section Placer, Sections To Sheet**: create plans and
  place views and sections on sheets.
- **Sheet Auto Rearrange, Sheet View Arrange**: rearrange sheet layouts.
  Sheet Auto Rearrange V029 adds Align & Distribute: views line up on shared row / column lines and spread
  across the usable area. V030 is the same engine in a redesigned window: large live preview, quick layouts
  and settings in three tabs.
- **Sheet Text Replace**: find and replace text notes in views on a sheet.
- **Sheet Views Combine** (Combine Sheets): list the views of two sheets in one grid, grouped by sheet and view type,
  and move the ticked ones onto one sheet (either sheet, another sheet or a new one) with the packing layout.
  Legends are copied to the same spot.

### DWG and families
- **DWG To DL(P), DWG To Lines**: convert DWGs to detail lines and hatches.
- **Export DWG to Folder, DWG Manager, DWG Statistics**: export, list, clean up and report CAD links and imports.
- **Batch Link DWG Family**: link DWGs into families in a batch.

### Detail lines
- **Detail Lines From Links, Detail Line Closed Loop, Auto Dim Detail Line**: generate detail lines from linked
  elements, close open loops, and dimension detail lines.
- **Detail Line Manager**: find, clean up and join detail lines, and rename line styles.

### Manage, worksets and memory
- **Annotation Overlap Detection**: find overlapping text notes, tags, room tags and dimensions, then move them
  apart. V006: By priority, with a recommended order (dimensions stay, text notes move first), presets and your own
  order; also Move Up, Move Down or Distribute. Runs in plans, sections, elevations, drafting views, legends and sheets.
- **ParaManager, Shared Parameter Studio, Schedule Export / Import**.
- **Link Version Checker**: status of RVT links, including ACC Desktop Connector and Autodesk Docs cloud links.
- **Delete Line Styles, Bulk Rename, Tracker ID**: clean up and rename styles and patterns, and give elements a
  permanent tracker ID.
- **Worksets & Elements Browser, Workset Creator, Workset Renamer (also from Excel), Delete Workset**.
- **WS Memory**: remember which worksets are open as named sets, then reopen the model with exactly those worksets.
- **Link Memory** (dropdown): V002 remembers which links (Revit, IFC, CAD, point clouds, images/PDF) the active view
  and its related views, or a print set, show, loads them back in one click, and exports the list to Excel.
  *Link Memory (classic)* (V001) remembers which Revit links are loaded as named sets.

### Coordination
- **Void Openings** (in development): openings for MEP through structure. So far: choose the structural model and
  the MEP model (the active model or a Revit link) and their categories, with element counts. Clash analysis, void
  placement and cutting follow.
- **Base Level to Parameter**: writes each element's base level and base offset to a text instance parameter as
  "Level 02 (+450)" (offset in mm). Lists the active view's model elements or the whole host model, checks the
  target parameter first (name, data type, binding, shared parameter file, categories, ownership), and can create
  the parameter as a shared instance Text parameter. One transaction; Ctrl+Z undoes it. The value can be the level
  name only or level name + offset in one of several patterns (or a custom one); elements already up to date are not rewritten.

## Installation

1. Download the latest release from the [Autodesk App Store](https://apps.autodesk.com).
2. Run the installer. It registers the add-in for Revit 2026 automatically.
3. Open Revit. The two SloperPro tabs appear in the ribbon.

## Version History

See [CHANGELOG.md](CHANGELOG.md) for the full history and per-tool versions.

### 6.37.0 — 2026-11-03
- New **Floor Views** panel (Documents tab) and **Floor EPS** on Floor Edit.

### 6.36.0 — 2026-11-03
- New **Floor Edit** panel: Floor Types, Name Builder, Floor Tag, Comparison and Elevation Sync.

### 6.35.0 — 2026-11-03
- New **Floor Lines** panel: Floor Crease Lines By Openings, By Points, and Floor Creaser.

### 6.34.0 — 2026-11-03
- New **Floor Points** panel: floor versions of the seven Shape Points tools.

### 6.33.0 — 2026-11-03
- **Floor Slope** panel: new Ridge and Multi Copies tools (in the By Point pulldown) and Floor Points & Slope.

### 6.32.0 — 2026-11-03
- **Align Spot Elevations 1.6.0**: line bend / end points up with a clicked tag; Quick Align Ends / Bends to Tag.

### 6.31.0 — 2026-11-02
- New **Floor Slope** panel: Floor Slope By Drain and Floor Slope By Point.

### 2.0.0 — 2026-10-02
- One button per tool; superseded tool versions removed
- Every tool now reports errors instead of failing silently, and shows its version in the window title

### 1.0.0 — 2026
- Initial public release on Autodesk App Store
- 30+ tools across Roof, Floor, View, Sheet, Setup, and Manage panels

## License

© 2026 MuhammedRafi. All rights reserved.
