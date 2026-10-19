# Tapered EPS 2D Pattern — V002 (Revit 2026)

Bumped from V001. V001 stays in its own folder and keeps its own ribbon button.

## What is new in V002

1. **Align the grid to any roof edge.** Board Settings → *Grid direction* = "Parallel to roof edge",
   then pick the edge in *Align to edge* (lists every edge with length and angle; the longest edge is
   pre-selected, so the default result is the same as V001). The chosen edge is drawn thick in the
   preview. With origin "Corner (flush with edges)" the grid starts on that edge, so full boards run
   along it. "Project X" is still available.
2. **A grid for each edge.** Board Settings → *Grid direction* = "Each edge (own grid per slope)".
   Every slope face (the part of the roof draining to one edge) gets its own board grid, parallel to
   that edge, with the first row flush on the edge. Each board row then covers exactly one thickness
   band. Board IDs carry the edge: `E1-R01-C01`. Grid lines stop at the hips/ridges and are not joined
   across faces. Where a contour lies exactly on a grid line (row spacing = contour spacing) the line is
   drawn once, as a grid line; the log says how many.
   12×8 roof, 1×1 m boards: 48 grid lines / 168 m (edge 1 and 3: 14 each, edge 2 and 4: 10 each),
   112 boards, 8 codes.
3. **Lines you can align / rotate in Revit.** Layers → *Output*:
   - **One detail group** — V001 behaviour (`EPS2D_<roofId>_<viewId>`).
   - **One group per layer** — `…_Grid`, `…_HipRidge`, `…_Contours`, `…_Labels`; move, rotate or align
     each layer on its own. With "Each edge" the grid is one group per edge: `…_Grid_E1`, `…_Grid_E2` …
   - **No group (loose lines)** — every detail line can be aligned, rotated or edited directly.
     "Replace previous" is disabled here, because loose lines can't be found again later.
   "Replace previous" removes the previous grouped pattern for the roof/view whichever group mode made it.
4. Window is a plain WPF `Window` (the `ToolWindowShell` reference was removed).

## Add to the ribbon menu

Put this in the ribbon setup, right after the Tapered EPS V001 / 2D V001 buttons in the Insulation panel.
Adjust `insulationPanel`, `assemblyPath` and the assembly name in the pack URI to match your ribbon code.

```csharp
var eps2dV002 = new PushButtonData(
    "TaperedEpsPattern2D_V002",
    "Tapered EPS\n2D Pattern V002",
    assemblyPath,
    "Revit26_Plugin_Revised.TaperedEpsPattern2D.V002.Commands.TaperedEpsPattern2DCommand")
{
    ToolTip = "Draw the tapered EPS board pattern as 2D detail lines in a plan view. " +
              "V002: align the board grid to any roof edge; output as one group, a group per layer, or loose lines.",
    Image = new BitmapImage(new Uri(
        "pack://application:,,,/Revit26_Plugin;component/Menu/RoofTools/Modelling/TaperedEpsPattern2D_V002/Resources/Icons/TaperedEpsPattern2D_V002_16.png")),
    LargeImage = new BitmapImage(new Uri(
        "pack://application:,,,/Revit26_Plugin;component/Menu/RoofTools/Modelling/TaperedEpsPattern2D_V002/Resources/Icons/TaperedEpsPattern2D_V002_32.png"))
};
insulationPanel.AddItem(eps2dV002);
```

If the Insulation tools sit in a pull-down instead of separate buttons, add it the same way:
`insulationPulldown.AddPushButton(eps2dV002);`

`.csproj` — register the icons:

```xml
<ItemGroup>
  <Resource Include="Menu\RoofTools\Modelling\TaperedEpsPattern2D_V002\Resources\Icons\TaperedEpsPattern2D_V002_16.png" />
  <Resource Include="Menu\RoofTools\Modelling\TaperedEpsPattern2D_V002\Resources\Icons\TaperedEpsPattern2D_V002_32.png" />
</ItemGroup>
```

## Still open (same as V001)

- `Core/` does not yet contain the copied `TaperedEpsLayout_V001/Core`; board pieces and codes come
  from the stand-ins `BoardPieceBuilder` and `BoardCodeAssigner`.
- Style keys are `DynamicResource` lookups (missing keys fall back to WPF defaults). Preview colour
  keys: `EpsPreview.GridBrush, EpsPreview.HipRidgeBrush, EpsPreview.ContourBrush, EpsPreview.OutlineBrush,
  EpsPreview.HoverBrush, EpsPreview.LabelBrush, EpsPreview.AlignEdgeBrush` (new).

## Files changed from V001

| File | Change |
|---|---|
| all | namespace `…TaperedEpsPattern2D.V002…`, `ToolInfo.Version = "V002"` |
| `Core/Pattern/GridDirectionMode.cs` | `SelectedEdge` added |
| `Core/Pattern/Pattern2DSettings.cs` | `AlignEdgeIndex` added |
| `Core/Pattern/GridFrame.cs` | uses the selected edge; `LongestEdgeIndex` added |
| `Core/Pattern/Pattern2DEngine.cs` | error when the selected edge does not exist |
| `Core/Pattern/OutlineEdgeInfo.cs` | **new** — edge list for the picker (length, angle, longest) |
| `Core/Pattern/EdgeGridBuilder.cs`, `EdgeGridSet.cs` | **new** — one grid per slope face, aligned to its edge |
| `Core/Pattern/GridDirectionMode.cs` | `EachEdge` added |
| `Core/Pattern/BoardPiece.cs`, `PatternSegment.cs` | `EdgeIndex` (which edge's grid) |
| `Core/Pattern/BoardPieceBuilder.cs` | cuts any region (outline or one face), ID prefix |
| `Core/Pattern/Pattern2DResult.cs` | `ContoursOnGridLines` |
| `Revit/GroupingMode.cs` | **new** — SingleGroup / GroupPerLayer / NoGroup |
| `Revit/DetailGroupManager.cs` | Replace deletes the base group and all `base_…` groups |
| `Revit/DetailPatternWriter.cs` | creates one group, one per layer, or none |
| `Revit/PatternWriteRequest.cs`, `PatternWriteResult.cs` | `Grouping`, `GroupNames` |
| `ViewModels/BoardSettingsViewModel.cs` | edge picker, highlight index |
| `ViewModels/LayersViewModel.cs` | Output choice replaces the Group checkbox |
| `ViewModels/TaperedEpsPattern2DViewModel.cs` | loads edges on open / Reset; passes grouping |
| `Views/PatternPreviewCanvas.cs` | draws the alignment edge |
| `Views/TaperedEpsPattern2DWindow.xaml(.cs)` | Align-to-edge and Output rows; plain `Window` |
| `Resources/Icons/*_V002_*.png` | icons with the aligned edge marked |
