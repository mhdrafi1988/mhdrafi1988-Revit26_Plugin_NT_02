# AutoSlope By Drain (Multi-Roof) V013

A new version folder (per TOOL_VERSIONING.md). V012 stays on disk and on the ribbon as the fallback.
V013 applies the AutoSlopeByPoint V032 review to the opening-based, multi-roof tool: the same fixes
where they apply, the same structure (ToolGuard, jobs through one ExternalEvent, ToolViewModelBase)
and the same window layout, adapted to roof tabs and the drain grid.
Preview of the window: the "AutoSlope By Drain V013" artifact.

> **Build status: not compiled yet.** This was written without Revit's API or a Windows .NET build.
> Every C# file passes a syntax parse, the XAML is well-formed, and every XAML binding, resource key
> and event handler was cross-checked against the ViewModels, `DrainResultRow`, `DrainItem`,
> `CircleMarkerGroup` and `Shared/SharedStyles.xaml` v3.1. Expect, at most, a few small compile fixes
> on the first build. Then run the test list below in Revit before putting it on the ribbon.

## Install into the repo

1. Copy the `AutoSlopeByDrain_V013` folder next to `AutoSlopeByDrain_V012`. It is an SDK-style
   project, so no `.csproj` edit is needed.
2. **ToolCatalog** (`Shared/Services/ToolCatalog.cs`). Without this line the code falls back to `13.0.0`.
   (The catalog currently lists the multi-roof tool only at V010; V012 has no entry.)
   ```csharp
   public static readonly ToolInfo MultiRoofSlopeByDrainV013 = Add("Auto Slope By Drain (Multi-Roof)", "13.0.0", "V013", "Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013");
   ```
3. **Ribbon** (`RoofToolsRibbon.cs`, By Drain pulldown). The command class keeps V012's name:
   ```csharp
   new PushButtonData("Btn_AutoSlopeByDrain_013", "By Drain V013", assemblyPath,
       "Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013.Commands.AutoSlopeByDrain")
   {
       ToolTip = ToolCatalog.MultiRoofSlopeByDrainV013.Tip("Slopes each picked roof towards its selected drain openings.")
   };
   ```
4. **CHANGELOG.md**: paste the entry at the bottom of this file.

No new NuGet packages (CommunityToolkit.Mvvm and EPPlus, as V012).

## What changed

### Fixes
| | V012 | V013 |
|---|---|---|
| Picking roofs | Enabled shape editing and reset every point to 0 at pick time, one committed undo step per roof, outside any group. Closing the window without running left the roofs flat. | Picking changes nothing. Each roof is reset at the start of its own run, inside its group. |
| Run again after a cancel | Roofs already sloped were not reset, so the next run read a sloped, multi-face top face. | Every run starts from a reset roof. |
| Failed or cancelled roof | Steps already committed stayed (inserted arc points, parameters, or the slope itself when a later step threw). | One TransactionGroup per roof inside the run's group: a roof that does not succeed is rolled back completely. Roofs that finished before a cancel stay applied, as agreed for V010. |
| Selected drain matching no shape point | Silently ignored. A roof whose drains matched nothing "succeeded" with no change and still wrote parameters. | Warning names each such drain; a roof with no matched drain point fails and rolls back. |
| Drain point matching | Only the shape points nearest each tessellated loop point (5 mm). | Every shape point within 5 mm of the opening edge, so points along a circle between tessellation points count too. |
| Drains selected count | Counted only drains visible under the size filter; Run used every checked drain. | Counts every checked drain and says how many are hidden by the filter. |
| Skipped count | Included the drain points themselves. | Drain points are not counted as skipped. (`AutoSlope_VerticesSkipped` changes meaning accordingly.) |
| Version in Excel and on the roof | Hard-coded "009". | From ToolCatalog. |
| Run duration | Whole seconds per roof, added up. | Wall-clock time of the batch, one decimal; start → end on the tile. Per-roof timing in each tab banner. |
| Detected-drains tile after a run | Replaced with the total of the roofs that ran. | Stays the total over all picked roofs. |
| Run after "Done" | Disabled for good. | "Run again" is available. |
| ExternalEvent | Static payload; result of `Raise()` ignored, so a refused request left the window on "Running". | One pending job; `Submit` checks `Accepted`. |
| Tab switch | Selected and showed the roof straight from the WPF event (outside API context). | Done through a job on the ExternalEvent. |
| While running | Inputs, drain ticks and marker settings stayed editable and the run read the marker groups live. | Inputs lock; marker settings are copied when Run starts. |
| Window | 1000 × 960 minimum: taller than a 1366 × 768 screen. | Opens at 1280 × 820, minimum 1100 × 640; the body scrolls. |
| Grid X / Y | Revit's internal feet, no unit. | Metres. |

### Speed (same results)
- Path graph: a plan grid finds the candidate pairs instead of comparing every point with every other
  point. The edge set and the order edges are added in are the same as V012's.
- Read-back after the run: a lookup instead of every point against every point, using the positions
  captured before commit (V012 read the pre-commit SlabShapeVertex handles after the commit). It is now
  **on by default**. Its ElevModel / ElevDiff columns go to the Excel workbook only; the window's
  Longest 10 table shows just PathLength_m, SlopePercent and ElevCalc_mm.

### Structure
- `ToolGuard.RunCommand` / `RunHandler`; one window at a time; owner set in the window.
- `AutoSlopeDrainHandler` + `AutoSlopeDrainEventManager.Submit` + `DrainJobs.cs`
  (`DrainRunJob`, `FocusRoofJob`, `ZoomJob`).
- ViewModel on `ToolViewModelBase` (IsRunning / Progress / SummaryText, 2,000-line log, log commands).
- `SettingsService` renamed `DrainSettingsStore` (it clashed with `Shared.Services.SettingsService`);
  new file `%AppData%\Revit26_Plugin\AutoSlopeByDrain\settings.v013.json`.
- Unused hex colour constants removed from `AppConstants`.

### Window layout
```
┌ Header: state · roofs · plan-view badges │ 6 tiles: Drains (of detected) · Processed · Skipped · Highest · Longest · Run time ┐
├ LEFT 300 ──────────────────────┬ MIDDLE ──────────────────────────────┬ RIGHT ─────────────────────────────┤
│ Every run: Slope % · Max path  │ [Roof 1 — … Done] [Roof 2 — … Ready] │ Longest 10 paths · Roof 1          │
│ ⚠ Run resets each roof to 0 … │ Detected drains  [All sizes▾][All]…  │ [Longest 10] [Skipped (n)]         │
│ OPTIONS · SET ONCE             │ ▾ Circle (4)              [All][None]│ #  PathLength_m SlopePercent ElevCalc_mm│
│ ▸ Path & curves   Edge 30 m …  │   ✓ Circle 100 x 100 mm 100 12.40 …  │ …10 rows…                          │
│ ▸ Circle markers  Drains · …   │ ▸ Rectangle (2)                      │ Hint            [Copy rows][Zoom to]│
│ ▸ Export & parameters  Excel … │ 3 of 7 drains checked · Roof Id …    │                                    │
│                                │ Done · 846 processed … · 2.9 s       │                                    │
├ Processing log (closed; latest line + counts) ─────────────────────────────────────────────────────────────┤
└ Footer: status strip (start · end · total) │ Open export folder                        Cancel · Run · Close ┘
```

## To check on the first Revit test
1. **Nested TransactionGroups**: one group per roof inside the run's group. Confirm one Undo step per run
   and that a failed roof leaves no trace (not even inserted arc points).
2. **Reset inside the run**: confirm `ResetShapePoints` → fresh top face gives the same drain matches as
   V012 did after its pick-time reset.
3. **Tab switch / Zoom to** run through the ExternalEvent; confirm the roof is selected and shown.
4. **Edge matching**: on a roof with ⌀100 openings, compare the "drain(s) → shape point(s)" log line
   with V012's "Matched N shape-edit vertex/vertices" line (V013 should be equal or higher).
5. **EPPlus licence**: `LicenseContext.NonCommercial` is kept from V012. Check it for commercial use
   (not legal advice).

## Test list
- [ ] Pick 3 roofs, close the window without running → all three roofs unchanged, nothing in Undo.
- [ ] Run 2 roofs (third has no drains ticked) → third tab "Skipped"; one Undo step undoes both roofs.
- [ ] Cancel during roof 2's "Building path graph" → roof 1 "Done" and applied, roof 2 "Rolled back"
      and unchanged, roof 3 "Not started".
- [ ] Run again after that cancel → roof 1 gives the same results as its first run.
- [ ] Tick a drain whose opening has no shape point → warning names it; if it is the only drain, the roof
      fails and is unchanged.
- [ ] Filter by size, tick drains, change the filter → count still includes hidden ticked drains and says so.
- [ ] Results panel: Longest 10 shows only PathLength_m, SlopePercent and ElevCalc_mm; row 1 = that
      roof's longest path; ElevCalc_mm = PathLength_m × SlopePercent × 10 (e.g. 16.42 m × 1.5 % → 246 mm);
      Skipped count = Skipped tile for a single-roof run; Copy rows pastes into 4 columns
      (#, PathLength_m, SlopePercent, ElevCalc_mm); Zoom to frames the rows.
- [ ] Skipped rows show "Beyond distance" with a path length, or "No path".
- [ ] Clean settings folder → defaults; all three sections closed; no Custom tags; read-back on.
- [ ] Change max edge distance → Path & curves header updates and shows Custom; Reset to defaults clears it.
- [ ] Non-plan active view → amber badge; markers skipped; slope applied.
- [ ] V012 vs V013 on the same flat roof and drains → same processed / highest / longest (skipped differs
      by the drain point count).
- [ ] Window at 1366 × 768 / 150 % scaling → usable through the body scroll.

## CHANGELOG entry

```
## 2026-10-14 — AutoSlope By Drain (Multi-Roof) V013

- New folder AutoSlopeByDrain_V013 (V012 kept as fallback). Same review as
  AutoSlopeByPoint V032, applied to the opening-based multi-roof tool.
- Fixes: picking roofs no longer changes the model (reset moved into each
  roof's run); one group per roof, so a failed or cancelled roof rolls back
  completely; drains with no matching shape point are reported and a roof
  with none fails; drain points matched along the whole opening edge;
  drains-selected count includes filtered-out ticks; skipped excludes drain
  points; version from ToolCatalog; wall-clock run time; Run again after a
  run; ExternalEvent request checked; inputs locked while running.
- Speed: plan-grid path graph and lookup read-back (same results);
  read-back on by default.
- Structure: ToolGuard; jobs through one ExternalEvent; ToolViewModelBase;
  DrainSettingsStore (settings.v013.json).
- Window: Every run card + three collapsed option sections with summaries,
  Custom tags and Reset to defaults; roof tabs with status pills; results
  side panel (Longest 10 / Skipped, Copy rows, Zoom to); log closed by
  default; 6 MetricTiles; 1280×820, min 1100×640; X/Y in metres.
- ToolCatalog: + MultiRoofSlopeByDrainV013 (13.0.0). Ribbon: By Drain + V013.
```
