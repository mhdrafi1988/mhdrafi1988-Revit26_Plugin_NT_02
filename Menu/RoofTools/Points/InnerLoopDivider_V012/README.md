# Inner Loop Divider — V012 (Revit 2026)

Bumped from V011 (V011 stays as it is). Folder `Menu/RoofTools/.../InnerLoopDivider_V012`
(put it next to V011), namespace `Revit26_Plugin_Revised.InnerLoopDivider.V012`.

Adds shape-editing points around a roof's openings (drains, rooflights, penetrations) so the
slope tools have vertices to work with.

## What changed from V011

### Fixes
| # | V011 | V012 |
|---|---|---|
| 1 | Opening the tool switched shape editing on and **reset every shape point to offset 0** — before the window even showed. Any slope already on the roof was lost, even if you just closed the window. | Launch is read-only. Resetting is an opt-in checkbox (**Reset existing offsets to 0 first**, off by default), done in the same transaction as Apply → one Undo. |
| 2 | "Div. Points = N" placed N points on **every curve** of the loop, ends included (rectangle 4 → 4 per side; two-arc circle 6 → 12 attempts). "Segment Length" (perimeter ÷ N) never matched. | N = points around the **whole loop**. Circles: evenly spaced from the east-most point. Polygons: corners kept, the rest shared over the sides by length. The **Gap** column is the real spacing. |
| 3 | A circle needed ≥ 3 arcs (Revit usually gives two half arcs → "Other"); arc sweep via `AngleTo` was wrong above 180°. | Circle = no sharp corners + constant radius (±1 %), any number of arcs; also many-sided polygons. Rectangle = 4 right-angle corners, split sides allowed. |
| 4 | Openings read only from a top face whose normal is exactly +Z (the real reason for the reset in #1); outer loop = longest perimeter; numbering restarted per face. | Read from the footprint sketch + holes in the top faces (`HostObjectUtils.GetTopFaces`) — works on sloped / shape-edited roofs; also finds shaft-opening cuts. Stable numbering (north→south, west→east). |
| 5 | Duplicates detected by `AddPoint` throwing; count unreliable. | Planned points within 5 mm of an existing shape point are skipped up front and counted (**Already There** tile / **Existing** column). |
| 6 | Status strip never moved; `Event.Raise()` result ignored; engine used the *active* document. | `IsRunning` / `SummaryText` set; a refused request is logged; requests carry the roof's own document. |

### New
- **Max spacing** mode: no gap along a loop longer than X m (circles get at least 3 points).
- Grid columns: **Size** (Ø or W × L, mm), **Source** (Sketch / Opening cut), **Total**, **New**, **Existing**, **Gap (m)**.
- Header tiles: Selected Shapes · **New Points** (exactly what Apply adds) · **Already There** · Total Openings.
- Each new point is placed **on** the current top surface (vertical projection), so adding points never changes the roof's shape.
- After Apply the counts refresh from the roof (no re-analyse, your row edits stay).

### Defaults changed (because N now means "around the loop")
Circular **6** (same) · Rectangular **8** (corners + one per side; V011's "4" really put 4 on every side) ·
Other **8**. Circles only are ticked when the tool is launched on its own (unchanged).

## Structure
```
Commands/InnerLoopDividerCommand.cs        pick (roof filter / preselection), read, open window
Core/Geometry/                             NO Revit API — unit-testable
  LoopSegments.cs   P2, LineSegment2D, ArcSegment2D
  Loop2D.cs         perimeter, area, centroid, corners, arc-length lookup, LoopChainer
  LoopClassifier.cs Circular / Rectangle / Other
  DivisionPlanner.cs Count / Spacing planning, existing-point matching
Core/Models/                               RoofLoopModel (+ live plan), Payload, Result
Core/Services/RoofGeometryService.cs       V011-compatible wrapper → RoofLoopReader
Core/Engine/InnerLoopDividerEngine.cs      Analyze / Apply dispatch
Infrastructure/Revit/                      CurveConverter, RoofLoopReader, ShapePointWriter
Infrastructure/ExternalEvents/             EventManager, Handler (TransactionGroup on the roof's document)
UI/                                        ViewModel, Window
```
`LoopDivisionService.cs` is gone (replaced by `ShapePointWriter`).

## CombinedRoofTools
Combined V003 still uses V011 — nothing changes for it. V012 keeps V011's public surface
(constructor `(UIApplication, ElementId, List<RoofLoopModel>, bool circlesOnlyByDefault)`,
`Loops`, `LoopsView`, `Log`, counts, `GlobalDivisionPoints`, commands, `SelectGroupLoops` /
`ClearGroupLoops`, `ApplyDivisionCompleted`, `RoofGeometryService.ExtractCircularLoops`), so moving
Combined to V012 is a namespace change in its Inner Loop Divider tab. Two things it will notice:
`QueuedPointCount` is now *new* points, and the default counts above.

## Add to the ribbon
In `RoofToolsRibbon.cs`, add V012 to the Inner Loop Divider pulldown next to V011
(same pattern as the other pulldowns — adjust the pulldown variable name):

```csharp
innerLoopDividerPulldown.AddPushButton(new PushButtonData(
    "Btn_InnerLoopDivider_V012",
    "Inner Loop Divider V012",
    assemblyPath,
    "Revit26_Plugin_Revised.InnerLoopDivider.V012.Commands.InnerLoopDividerCommand")
{
    ToolTip = "V012 — adds shape points around roof openings. Read-only launch (no offset reset), " +
              "points per loop or max spacing, exact new / existing counts."
});
```

## Test checklist (Revit)
1. Flat roof with a sketched circular drain → shows **Circular**, 6 → 6 new points evenly around it.
2. Roof already sloped by AutoSlope → open the tool, close it → slope unchanged.
3. Rectangle opening, 8 → 4 new (the 4 corners already exist once shape editing is on).
4. Apply twice → second time **New = 0**, nothing added.
5. Shaft opening through the roof → listed with Source = *Opening cut*.
6. Tick *Reset existing offsets first* → roof flattens and points are added; one Undo restores both.
