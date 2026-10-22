# AutoSlopeByPoint V032

Current version: **32.3.0** (`ToolCatalog.AutoSlopeByPointV032`). Release notes are in the repo's `CHANGELOG.md`.

V032 is the current Auto Slope By Point tool. V030 stays on disk and on the ribbon as the fallback.
V032 contains everything from V031 (a review build that was never wired in), plus the collapsed option
sections and the results side panel. The change codes (B1, D1, …) below come from the V030 refactor
review plan, which is not kept in this repo.

> **Build:** run `dotnet build` as described in `CLAUDE.md` before every release. The 32.3.0 change
> was written in a sandbox without the .NET SDK, so it has not been compiled there.

## Where it is wired in

- **ToolCatalog** (`Shared/Services/ToolCatalog.cs`):
  ```csharp
  public static readonly ToolInfo AutoSlopeByPointV032 = Add("Auto Slope By Point", "32.3.0", "V032", "Revit26_Plugin_Revised.AutoSlopeByPoint.V032");
  ```
  Without this entry the code falls back to `32.0.0` (`AutoSlopeConstants.FallbackToolVersion`).
- **Ribbon** (`Menu/00_Push_Button_Menu_items/Ribbon/RoofToolsRibbon.cs`): button `Btn_AutoSlopeByPoint_032`
  ("By Point V032"), command `Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Commands.AutoSlopeCommand`.
  It is first in the "By Point" pulldown and supplies the pulldown's icon. V030, Ridge V003 and
  Multi Copies 30 are listed below it.

No new NuGet packages; it uses CommunityToolkit.Mvvm and EPPlus, as V030 does.

## Window layout

Default size 1000 × 640, minimum 880 × 520. The window title reads "Auto Slope By Point — v32.3.0".

```
┌ Header: state · roof · plan-view badges │ 6 tiles: Processed · Skipped · Drains picked → used · Highest elevation · Longest path · Run time ┐
├ LEFT ─────────────────────────────────────────┬ RIGHT ──────────────────────────────────────────────┤
│ Every run: Drains [Re-pick] · Slope (%) ·     │ Longest 10 paths     [Longest 10] [Skipped (n)]      │
│            Drainage Distance (m)              │ # PathLength_m ElevModel_mm 1 % 1.5 % 2 % (mm)       │
│ ⚠ Run resets every existing shape point …    │ …10 rows…                                            │
│ OPTIONS · SET ONCE, KEPT BETWEEN SESSIONS     │ Hint                         [Copy rows][Zoom to]    │
│ ▸ Drain options      Snap to nearest · 500 mm │                                                      │
│ ▸ Path & curves      Max link: automatic · on │                                                      │
│ ▸ Circle markers     Drains · Highest · …     │                                                      │
│ ▸ Export & parameters  Excel · …\AutoSlope_…  │                                                      │
├ Processing log (closed; latest line + counts) ──────────────────────────────────────────────────────┤
└ Footer: status strip (start · end · total) │ Export results…            Cancel · Run AutoSlope · Close ┘
```

- **Every run** card: the inputs most runs need, in one row: drains (count + Re-pick), slope (presets
  0.5 – 2.5 % or any value 0.1 – 10 %) and drainage distance (0.5 – 500 m). The slope hint shows the
  rise per 10 m.
- **Option sections** start closed. Each header shows its current values in one line and gets a blue
  **Custom** tag when anything inside differs from the defaults. Each section has **Reset to defaults**.
  Which sections were left open is saved between sessions.
- **Circle markers**: one row per group (on/off, line style, colour, radius) under one column heading,
  with the group's note and options below it. Markers are drawn only when the active view is a plan view.
  - **Drains**: a circle on each drain point.
  - **Highest point**: a circle on every point tied at the highest calculated elevation.
    - **Add an “Offset: X mm” text note with a leader** (on by default): a red note next to each highest point.
    - **Also add it at every roof boundary corner** (32.3.0, off by default; available only while the
      note above is ticked): the same note at every corner of the roof's outer boundary, showing that
      corner's calculated elevation. Openings are ignored, no circle is drawn, and a corner that already
      has the highest-point note is not labelled twice. A corner needs a shape point within 50 mm.
  - **Allowed offset**: a circle on every point at or above the threshold (default 500 mm), except
    points already marked highest.
- **Results side panel**: an Excel-style grid.
  - **Longest 10** lists the ten longest drain paths, longest first: `#`, `PathLength_m`, `ElevModel_mm`
    (read back from Revit) and the elevation that path gives at **1 %**, **1.5 %** and **2 %**
    (path length × slope, in mm).
  - **Skipped** lists points left at 0: `#`, `VertexIndex`, `Status` ("No path" / "Beyond distance"),
    `PathLength_m` and a suggested fix. Points with no path come first, then the ones closest to the
    distance limit. The title shows "first 10 of n" when more were skipped.
  - **Copy rows** copies the selected rows, or the whole tab, with a header line, as tab-separated
    columns that paste into Excel (Longest: 6 columns; Skipped: 5 columns).
  - **Zoom to** selects the roof and zooms the active view to the selected points. Revit's API can't
    select individual roof shape points, so selecting the roof is as close as it gets. With the roof
    selected and Modify Sub Elements on, the points are visible.
- The Excel workbook (Vertex Data + Run Summary sheets) keeps its full column set; only the on-screen
  Longest 10 table is narrower.

## Defaults

| Setting | Default |
|---|---|
| Slope | 1.5 % |
| Drainage distance | 100 m |
| Max link length | 0 (= drainage distance, same graph as V030) |
| Drain mode | Snap to the nearest shape point |
| Use nearby shape points | On, 500 mm radius |
| Insert arc points | On |
| Marker colours | Drains Blue · Highest point Red · Allowed offset Orange |
| Marker radius | 250 mm, line style "Thin Lines" |
| Offset text note / at boundary corners | On / Off |
| Allowed offset threshold | 500 mm |
| Export to Excel | On, `Documents\AutoSlope_Reports`, don't ask to open |

Settings: `%AppData%\Revit26_Plugin_Revised\AutoSlopeByPoint\settings.v032.json` (its own file; V030 never
reads it). Settings files saved before 32.3.0 load unchanged, and the corner-note option reads as off.

## Everything carried over from V031

| Code | Change |
|---|---|
| B1 | Cancel or any failure rolls back the whole run. Only a successful run calls `Assimilate()`. |
| B2 | First-run defaults: 1.5 %, 100 m, 500 mm, Blue / Red / Orange markers. |
| B3 | Max drainage distance and max link length are separate decimal inputs (link length 0 = same as V030). |
| B4–B11 | No garbled text; version from ToolCatalog; inputs snapshotted and locked while running; real start / end / total; guarded file opens; each error logged once. |
| D1–D7 | Exact drain point set; Drain mode (snap / add a point); plan-distance matching; drain index taken from the actual path; skipped reasons; checks before any change; warning when Revit adjusts points. |
| Structure | Core is Revit-free; the engine is split into step classes; ViewModel on `ToolViewModelBase`; ToolGuard on command and handler; one window at a time; owner set; spatial-grid graph and `PriorityQueue`. |

The face-sampling link test (P1c) is unchanged on purpose. Replacing it changes which links are
accepted, so compare real roofs first.

## To check in Revit
1. **Re-pick** and **Zoom to** call `Selection.PickObjects` and `UIView.ZoomAndCenterRectangle` from the
   ExternalEvent handler. Both normally work from a modeless window; confirm them.
2. **Add a shape point at each pick**: confirm the added point lands flat on the reset roof.
3. **Cancel during "Applying elevations"**: confirm the run stops and the roof is unchanged.
4. **EPPlus licence**: `LicenseContext.NonCommercial` is kept from V030. Check it for commercial use (not legal advice).

## Test list
- [ ] Cancel during "Building path graph" → the roof is unchanged.
- [ ] Clean settings folder → defaults; all four sections closed; no Custom tags.
- [ ] Change the nearby radius → the Drain options header updates and shows Custom; Reset to defaults clears it.
- [ ] Leave Circle markers open, close the window, reopen → it is still open.
- [ ] After a run, Longest 10 row 1 = the Longest path tile; the 1.5 % column = PathLength_m × 15.
- [ ] Skipped tab count = the Skipped tile.
- [ ] Select 3 rows → Zoom to frames them and selects the roof; Copy rows → paste into Excel lands in 6 columns (Longest) or 5 (Skipped).
- [ ] Snap mode, pick mid-panel → the run succeeds; Add mode → a new point at the pick becomes the drain.
- [ ] Pick next to an opening with a 500 mm radius → only points on the picked edge or opening become drains.
- [ ] Run, then Run again → same drain count; one undo step per run.
- [ ] Non-plan active view → amber badge; markers and offset notes skipped; slope applied.
- [ ] Corner notes on: rectangular roof → 4 corner notes; L-shaped roof → 6; roof with an opening → notes on the outer edge only.
- [ ] Corner notes on, highest point at a corner → that corner has one note, not two.
- [ ] Offset text note unticked → the corner option is greyed out and no notes are placed.
- [ ] V030 vs V032 on the same roof (default link length) → same processed / skipped / highest.
- [ ] Window at 1366×768 / 150 % scaling → usable through the body scroll (results grid scrolls inside).
