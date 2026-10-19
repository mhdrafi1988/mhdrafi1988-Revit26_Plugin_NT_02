# Tapered EPS 2D Pattern — V004 (Revit 2026)

Bumped from V003 (V003 stays as it is, with its own button). Folder:
`Menu/RoofTools/Modelling/TaperedEpsPattern2D_V004`, namespace `Revit26_Plugin_Revised.TaperedEpsPattern2D.V004`.

## What is new in V004 — choose where rows start and where the leftover cut goes

Board Settings → **Row start** (contour rows mode only):

| Option | Rows start | Leftover cut | Notes |
|---|---|---|---|
| **A** (default) | at both hips | one filler in the middle | hips mirrored at every corner (V003) |
| **B** | at both hips | split equally into the two hip ends | no middle cut; each row symmetric |
| **C** | at the left hip | at the right-hand hip (looking in from the edge) | no middle cut |
| **D** | at the exact middle of the row | at both hips | no middle cut; joints line up across rows |

**Middle start** (only for D): *Auto per edge* (default) · *D1 joint on centre line* · *D2 board centred*.
Auto lays each face both ways and keeps the one with fewer slivers, then more full boards; the log says
which it chose per edge.

**Min cut piece** (default 0.25 m): pieces narrower than this along the row are "slivers".
- A, B, C avoid them: a cut under the minimum is spread over one more board
  (A: centred full board with two equal cuts; B: one board fewer, two bigger hip ends; C: last two pieces equal).
- D keeps its set-out (that is the point of starting from the middle); Auto picks the variant with fewest
  slivers, and any that remain are counted in the summary line and the log.
- A row no longer than one board is always one piece.

Without a middle cut the leftover moves to the hips, so the hip usually cuts two boards per row
instead of one — fewer straight cuts, more hip cuts. The summary line under Board Settings shows
`full · hip cuts · straight cuts · ridge row · slivers` for the current choice, so you can compare before Run.

12.4 × 8.6 m roof, 1 % slope, 1.0 × 1.0 m boards:

| Option | Full | Hip cuts | Straight cuts | Ridge row | Slivers |
|---|---|---|---|---|---|
| A | 80 | 32 | 16 | 12 | 0 |
| B | 72 | 64 | 0 | 12 | 0 |
| C | 80 | 48 | 0 | 12 | 0 |
| D1 | 80 | 64 | 0 | 14 | 20 |
| D2 | 66 | 62 | 0 | 12 | 0 |
| D Auto | 72 | 64 | 0 | 12 | 0 (edges 1/3 → D2, edges 2/4 → D1) |

Piece kinds in V004: any piece cut by a hip is a hip piece (`H…-L` / `H…-R`; pieces with the same
mirrored shape share the number); a rectangular part-board is a straight cut (`C…`).

---

## Contour-row layout (from V003)

Board Settings → *Grid direction* = **Contour rows (each edge, mirrored hips)**. On every slope face:

1. **Rows are the bands between thickness contours.** Row n runs parallel to the roof edge, from
   (n−1)·width to n·width away from it, so each board tapers exactly one band (1.0 m × 1 % = 10 mm).
   Cross joints are perpendicular to the contours.
2. **Full boards wherever possible.** Each row starts at the hip on both ends and fills inward with
   full boards.
3. **Cut only where needed:**
   - **Hip pieces** at both ends of a row. They have the same shape in every row and are mirror images
     of the neighbouring face's pieces across the hip. Code: `H01-L` / `H01-R` (same number = mirrored pair).
   - **One filler in the middle of the row.** If the gap is more than one board, a full board is centred
     with two equal slips; if those slips would be under 0.25 m, the gap is split into two equal halves.
   - **Ridge row**: the last row at the ridge/apex takes what is left (e.g. 0.30 m when the half-width is 4.30 m).
     This can't be moved — rows are fixed by thickness counted from the edge.
4. **Contours follow board rows** (Layers card, on by default): contour interval = board width × slope.
   Contours then lie exactly on the row lines and are drawn once, as grid lines. Untick to set your own interval.
5. **Codes**: `F01…` full boards (one per thickness band), `H01-L/R…` hip pieces, `C01…` every other cut
   (one per shape and band). Numbers run from the thinnest band up.
6. **Grid lines are continuous** along each line within a face (a row line runs hip to hip as one line;
   aligned joints across rows become one line).
7. Preview tints pieces by kind (full / hip / filler / ridge row); hover shows ID, code, thickness, kind.
   The Boards tile shows "pieces (full)".

Example — 12.4 × 8.6 m roof, 1 % slope, 20 mm min, 1.0 × 1.0 m boards:
80 full, 32 hip pieces (16 mirrored L/R pairs, all half-board triangles), 16 fillers (0.40 / 0.60 m),
12 ridge-row pieces; 56 grid lines; codes F01–F04, H01–H04 (L/R), C01–C13.

On irregular roofs the mirror holds along every hip; in the top rows of very uneven faces, where the
boundary between faces bends, some hip pieces have no mirror partner (pentagon test: 38 of 46 paired).

The single-grid modes from V002 are still there: *One grid parallel to a roof edge* (pick the edge)
and *One grid along Project X*. Output grouping (one group / group per layer with grid per edge /
loose lines) is unchanged from V002.

## Add to the ribbon

Add this to the panel where your Modelling tools sit, next to the V003 button.
Adjust the panel variable, `assemblyPath` and the assembly name in the pack URI to match your ribbon code.

```csharp
var eps2dV004 = new PushButtonData(
    "TaperedEpsPattern2D_V004",
    "Tapered EPS\n2D Pattern V004",
    assemblyPath,
    "Revit26_Plugin_Revised.TaperedEpsPattern2D.V004.Commands.TaperedEpsPattern2DCommand")
{
    ToolTip = "Tapered EPS board pattern as 2D detail lines: rows between thickness contours, " +
              "full boards from the hips, mirrored hip pieces, one filler per row.",
    Image = new BitmapImage(new Uri(
        "pack://application:,,,/Revit26_Plugin;component/Menu/RoofTools/Modelling/TaperedEpsPattern2D_V004/Resources/Icons/TaperedEpsPattern2D_V004_16.png")),
    LargeImage = new BitmapImage(new Uri(
        "pack://application:,,,/Revit26_Plugin;component/Menu/RoofTools/Modelling/TaperedEpsPattern2D_V004/Resources/Icons/TaperedEpsPattern2D_V004_32.png"))
};
modellingPanel.AddItem(eps2dV004);
```

`.csproj`:

```xml
<ItemGroup>
  <Resource Include="Menu\RoofTools\Modelling\TaperedEpsPattern2D_V004\Resources\Icons\TaperedEpsPattern2D_V004_16.png" />
  <Resource Include="Menu\RoofTools\Modelling\TaperedEpsPattern2D_V004\Resources\Icons\TaperedEpsPattern2D_V004_32.png" />
</ItemGroup>
```

New optional preview colour keys (fall back to system colours): `EpsPreview.FullPieceBrush,
EpsPreview.HipPieceBrush, EpsPreview.FillerPieceBrush, EpsPreview.RidgeRowBrush`.

## Files changed from V003

| File | Change |
|---|---|
| all | namespace `…V004…`, `ToolInfo.Version = "V004"`, icons renamed |
| `Core/Pattern/RowStartMode.cs`, `MiddleStartVariant.cs` | **new** — options A/B/C/D and D1/D2/Auto |
| `Core/Pattern/RowJoints.cs` | **new** — joint set-out per option, min-piece rules |
| `Core/Pattern/RowLayoutBuilder.cs` | uses `RowJoints`; Auto per face; hip pieces = any piece cut by a hip; sliver count |
| `Core/Pattern/Pattern2DSettings.cs` | `RowStart`, `MiddleVariant`, `MinPieceM` |
| `Core/Pattern/Pattern2DResult.cs`, `RowLayoutResult.cs` | `SliverCount`, `LayoutNotes` |
| `Core/Pattern/Pattern2DEngine.cs`, `SettingsValidator.cs` | pass-through, min-piece range |
| `ViewModels/BoardSettingsViewModel.cs` | Row start, Middle start, Min cut piece |
| `ViewModels/TaperedEpsPattern2DViewModel.cs` | summary line, log notes and sliver warning on Run |
| `Views/TaperedEpsPattern2DWindow.xaml` | three new rows + summary line in Board Settings |

## Files changed from V002 (in V003)

| File | Change |
|---|---|
| all | namespace `…V003…`, `ToolInfo.Version = "V003"`, icons renamed |
| `Core/Pattern/RowLayoutBuilder.cs` | **new** — contour-row layout (rows, hip anchoring, filler rule, piece kinds) |
| `Core/Pattern/RowPieceCoder.cs` | **new** — F / H-L / H-R / C codes |
| `Core/Pattern/CollinearUnion.cs` | **new** — joins board edges into continuous grid lines |
| `Core/Pattern/PieceKind.cs`, `RowLayoutResult.cs` | **new** |
| `Core/Pattern/EdgeGridBuilder.cs`, `EdgeGridSet.cs` | removed (replaced by the row layout) |
| `Core/Pattern/BoardPiece.cs` | `Kind`, `ShapeKey` |
| `Core/Pattern/Pattern2DSettings.cs` | default mode = contour rows; `ContoursFollowRows`, `EffectiveContourIntervalMm` |
| `Core/Pattern/Pattern2DEngine.cs`, `SettingsValidator.cs`, `GridDirectionMode.cs` | row layout + interval lock |
| `ViewModels/*`, `Views/*` | mode list, contour lock checkbox, Boards tile, preview tint + hover kind |
