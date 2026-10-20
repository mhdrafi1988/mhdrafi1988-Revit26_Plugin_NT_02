# AutoSlopeByPoint V032

A new version folder (per TOOL_VERSIONING.md). V030 stays on disk and on the ribbon as the fallback.
V032 contains everything that was in V031, which was never wired in, plus the second-round layout:
collapsed option sections and a results side panel. Change codes (B1, D1, …) refer to
`claude/AutoSlopeByPoint_V030_Refactor_Plan.md`.

> **Build status: not compiled yet.** This was written without Revit's API or a Windows .NET build.
> Every C# file passes a syntax parse, the XAML is well-formed, and every XAML binding and resource
> key was cross-checked against the ViewModels, `ResultRow` and `Shared/SharedStyles.xaml` v3.1.
> Expect, at most, a few small compile fixes on the first build. Then run the test list below in Revit
> before putting it on the ribbon.

## Install into the repo

1. Copy the `AutoSlopeByPoint_V032` folder next to `AutoSlopeByPoint_V030` under `Menu/…`.
   It is an SDK-style project, so no `.csproj` edit is needed.
2. **ToolCatalog** (`Shared/Services/ToolCatalog.cs`). Without this line the code falls back to `32.0.0`.
   ```csharp
   public static readonly ToolInfo AutoSlopeByPointV032 = Add("Auto Slope By Point", "32.0.0", "V032", "Revit26_Plugin_Revised.AutoSlopeByPoint.V032");
   ```
3. **Ribbon** (`RoofToolsRibbon.cs`, "By Point" pulldown):
   ```csharp
   new PushButtonData("Btn_AutoSlopeByPoint_032", "By Point V032", assemblyPath,
       "Revit26_Plugin_Revised.AutoSlopeByPoint.V032.Commands.AutoSlopeCommand")
   {
       ToolTip = ToolCatalog.AutoSlopeByPointV032.Tip("Slopes a roof towards picked drain points.")
   };
   ```
4. **CHANGELOG.md**: paste the entry at the bottom of this file.

No new NuGet packages; it uses CommunityToolkit.Mvvm and EPPlus, as V030 does.

## Window layout (new in V032)

```
┌ Header: state · roof · plan-view badges │ 6 tiles: Processed · Skipped · Drains · Highest · Longest · Run time ┐
├ LEFT ─────────────────────────────────────────┬ RIGHT ───────────────────────────────────────────┤
│ Every run: Drains [Re-pick] · Slope % · Max   │ Longest 10 paths  [Longest 10] [Skipped (n)]     │
│            drainage distance                  │ # VertexIndex DrainIndex PathLength_m ElevCalc_mm │
│ ⚠ Run resets every existing shape point …    │   ElevModel_mm ElevDiff_mm  (Excel-style grid)    │
│ OPTIONS · SET ONCE                            │ …10 rows…                                         │
│ ▸ Drain options      Snap to nearest · 500 mm │ Hint                        [Copy rows][Zoom to]  │
│ ▸ Path & curves      Max link: automatic · on │                                                   │
│ ▸ Circle markers     Drains · Highest · …     │                                                   │
│ ▸ Export & parameters  Excel · …\AutoSlope_…  │                                                   │
├ Processing log (closed; latest line + counts) ─────────────────────────────────────────────────────┤
└ Footer: status strip (start · end · total) │ Export results…            Cancel · Run · Close ┘
```

- **Every run** card: the only inputs most runs need, in one row.
- **Option sections** start closed. Each header shows its current values in one line and gets a blue
  **Custom** tag when anything inside differs from the defaults. Each section has **Reset to defaults**.
  Which sections were left open is saved between sessions.
- **Results side panel**: an Excel-style grid with the same column names as the "Vertex Data" sheet.
  - **Longest 10** lists the ten longest drain paths, longest first. ElevDiff is green when Revit kept the
    calculated height and orange when Revit adjusted it.
  - **Skipped** lists points left at 0 ("Beyond distance" / "No path") with a suggested fix. Points with
    no path come first, then the ones closest to the distance limit.
  - **Copy rows** copies the selected rows, or the whole tab, as tab-separated columns that paste into Excel.
  - **Zoom to** selects the roof and zooms the active view to the selected points. Revit's API can't
    select individual roof shape points, so selecting the roof is as close as it gets. With the roof
    selected and Modify Sub Elements on, the points are visible.

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

Settings: `%AppData%\Revit26_Plugin\AutoSlopeByPoint\settings.v032.json` (a new file).

## To check on the first Revit test
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
- [ ] After a run, Longest 10 row 1 = the Longest path tile; ElevCalc / ElevModel match the Excel workbook rows.
- [ ] Skipped tab count = the Skipped tile.
- [ ] Select 3 rows → Zoom to frames them and selects the roof; Copy rows → paste into Excel lands in 7 columns.
- [ ] Snap mode, pick mid-panel → the run succeeds; Add mode → a new point at the pick becomes the drain.
- [ ] Pick next to an opening with a 500 mm radius → only points on the picked edge or opening become drains.
- [ ] Run, then Run again → same drain count; one undo step per run.
- [ ] Non-plan active view → amber badge; markers skipped; slope applied.
- [ ] V030 vs V032 on the same roof (default link length) → same processed / skipped / highest.
- [ ] Window at 1366×768 / 150 % scaling → usable through the body scroll (results grid scrolls inside).

## CHANGELOG entry

```
## 2026-10-14 — AutoSlopeByPoint V032

- New folder AutoSlopeByPoint_V032 (V030 kept as fallback; V031 was a
  review build and is not wired in). Review: claude/AutoSlopeByPoint_V030_Refactor_Plan.md.
- Fixes: cancel/failure roll back the whole run; first-run defaults;
  drainage distance and max link length split; garbled text; version from
  ToolCatalog; inputs locked while running; real start/end/total times.
- Drains: exact drain point set, Drain mode (snap / add point), plan-distance
  matching, path-based drain index, read-only checks before any change.
- Structure: Core Revit-free; engine split into steps; ToolViewModelBase;
  ToolGuard on command + handler; single window; owner set.
- Window: "Every run" card + four collapsed option sections with summaries,
  Custom tags and Reset to defaults (open state remembered); results side
  panel with Longest 10 paths / Skipped points in the Vertex Data columns,
  Copy rows and Zoom to; log closed by default; 6 MetricTiles; min 960×620.
- ToolCatalog: + AutoSlopeByPointV032 (32.0.0). Ribbon: By Point pulldown + V032.
```
