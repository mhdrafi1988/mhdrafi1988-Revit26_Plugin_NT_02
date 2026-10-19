# Tapered EPS 2D Pattern — V001 (Revit 2026)

Draws the tapered EPS board pattern of a footprint roof as **2D detail lines** in the active plan
view: continuous board grid lines, hips/ridges, thickness contours, and optional board labels
(text notes), optionally wrapped in one detail group `EPS2D_<roofId>_<viewId>`.
No 3D elements, DirectShapes, shared parameters or other model changes.

## Status

| Part | State |
|---|---|
| `Core/Pattern` (geometry, pure C#) | Built and tested: 29 tests passing (offline xUnit-compatible runner) |
| `Revit/`, `Events/`, `Commands/` | Written against the Revit 2026 API, **not compiled yet** (no RevitAPI.dll available while writing) |
| `ViewModels/`, `Views/` | Written for CommunityToolkit.Mvvm 8 / WPF, **not compiled yet** |
| Copy of `TaperedEpsLayout_V001/Core` | **Not done** — the repo was not available (see below) |

## Integration checklist

1. **Copy the V001 Core.** Copy `TaperedEpsLayout_V001/Core/*` into `Core/` here, re-namespaced to
   `Revit26_Plugin_Revised.TaperedEpsPattern2D.V001.Core`. `Core/Pattern` does not depend on it.
   Board pieces and codes are currently produced by two stand-ins:
   `BoardPieceBuilder` (grid cutting) and `BoardCodeAssigner` (codes `F01…` full, `C01…` cut,
   grouped by full/cut + min/max thickness rounded to 1 mm). If board/code counts must match the 3D
   tool exactly, swap these two for `GridCutter` / `PieceGrouper` inside `Pattern2DEngine.Generate`.
2. **ToolWindowShell / SharedStyles (verify).** The window assumes
   `Revit26_Plugin_Revised.Shared.UI.ToolWindowShell` (a `Window` subclass). Fix the `xmlns:shell` in
   `Views/TaperedEpsPattern2DWindow.xaml` and the `using` in the code-behind if it lives elsewhere.
   All styles are `DynamicResource` keys, so missing keys fall back to WPF defaults instead of
   crashing. Keys used:
   `ToolHeaderTitle, ToolHeaderMeta, MetricTile, MetricTileValue, MetricTileLabel, Card, CardTitle,
   Badge, FieldLabel, FieldTextBox, ErrorText, SummaryGrid, SecondaryButton, RunButton`.
   Preview colour tokens to add to SharedStyles (the canvas falls back to system colours):
   `EpsPreview.GridBrush, EpsPreview.HipRidgeBrush, EpsPreview.ContourBrush,
   EpsPreview.OutlineBrush, EpsPreview.HoverBrush, EpsPreview.LabelBrush`.
3. **Ribbon** — next to Tapered EPS V001 in the Insulation panel:

   ```csharp
   var eps2d = new PushButtonData(
       "TaperedEpsPattern2D_V001", "Tapered EPS\n2D Pattern", assemblyPath,
       TaperedEpsPattern2DToolInfo.CommandClass)
   {
       ToolTip = "Draw the tapered EPS board pattern as 2D detail lines in a plan view.",
       Image = new BitmapImage(new Uri("pack://application:,,,/Revit26_Plugin;component/Menu/RoofTools/Insulation/TaperedEpsPattern2D_V001/Resources/Icons/TaperedEpsPattern2D_16.png")),
       LargeImage = new BitmapImage(new Uri("pack://application:,,,/Revit26_Plugin;component/Menu/RoofTools/Insulation/TaperedEpsPattern2D_V001/Resources/Icons/TaperedEpsPattern2D_32.png"))
   };
   insulationPanel.AddItem(eps2d); // immediately after the Tapered EPS V001 button
   ```

4. **.csproj** — register the icons:

   ```xml
   <ItemGroup>
     <Resource Include="Menu\RoofTools\Insulation\TaperedEpsPattern2D_V001\Resources\Icons\TaperedEpsPattern2D_16.png" />
     <Resource Include="Menu\RoofTools\Insulation\TaperedEpsPattern2D_V001\Resources\Icons\TaperedEpsPattern2D_32.png" />
   </ItemGroup>
   ```

5. **Tests** — add `Tests/TaperedEpsPattern2D_V001/*.cs` to the xUnit project. If that project does
   not reference the plugin assembly, link the Core sources:
   `<Compile Include="..\Menu\RoofTools\Insulation\TaperedEpsPattern2D_V001\Core\Pattern\**\*.cs" />`.
   Then add the copied V001 engine tests (re-namespaced) once step 1 is done.

## Decisions taken (defaults from the plan; change if you disagree)

- **Hip merge rule:** collinear hip/ridge pieces merge only through a vertex where exactly two
  lines meet. Otherwise the 10×10 square's opposite hips would merge into 2 lines instead of 4.
- **Contour levels:** h = tmin + n·interval, n ≥ 1. The tmin loop (= outline) is not drawn; the
  peak level drops out as a degenerate loop (< 1e-4 m²).
- **Grid lines on the outline are not drawn** (needed for the 172 m total).
- **Board length** runs along the grid direction, **width** across it.
- **Project X** = Revit internal X axis.
- **Groups:** the name is the GroupType name; Replace deletes that GroupType (and its instances).
  Replace is disabled when Group is off. If the name is taken and Replace is off, `_2`, `_3`… is added.
- **Labels:** one per board (full and cut), centred on the board, text notes inside the group.
- **Reset** restores default settings *and* re-reads the roof from the model.
- **Z of detail lines:** `GenLevel.ProjectElevation` (elevation from the internal origin) rather than
  `Elevation`, which is relative to the project's elevation base and can differ.
- **Line style names** are matched ignoring angle brackets and case (`<Thin Lines>` = `Thin Lines`).
  Styles Revit rejects for detail lines are filtered out, and the writer double-checks with
  `CurveElement.GetLineStyleIds()`.
- **Minimum segment length** = max(1 mm, Revit ShortCurveTolerance).
- **Warnings** such as "Line is slightly off axis" are suppressed by a failures preprocessor and
  listed in the log; errors still roll the whole transaction back.
- **Preview** recomputes 150 ms after the last change, off the UI thread. Run always recomputes
  first if a change is pending, so the lines written are exactly the ones previewed.

## Classes

| Folder | Classes |
|---|---|
| `Commands` | `TaperedEpsPattern2DCommand` |
| `Core/Pattern` | `Vec2, Segment2D, Polygon2D, HalfPlane, ConvexClipper, GeometryTolerance, EdgeDistanceField, ThicknessField, GridFrame, GridLineBuilder, SlopeFace, SlopeFaceBuilder, HipRidgeExtractor, SegmentSplitter, SegmentDeduplicator, SegmentMerger, ContourLoop, ContourBuilder, BoardPiece, BoardPieceBuilder, BoardCodeAssigner, PatternLabel, BoardLabelBuilder, PatternSegment, LayerStats, Pattern2DResult, Pattern2DEngine, Pattern2DSettings, SettingsValidator, LoopChainer, OutlineValidator, OutlineValidationResult` + enums `GridDirectionMode, GridOriginMode, LabelMode, PatternLayer` |
| `Revit` | `PlanViewValidator, RoofSelectionFilter, RoofPicker, UnitConverter, RoofFootprintReader, RoofContext, RoofContextBuilder, LineStyleOption, LineStyleProvider, TextNoteTypeOption, TextNoteTypeProvider, PatternWriteRequest, PatternWriteResult, DetailGroupManager, DetailPatternWriter, WarningSwallower` |
| `Events` | `RunPatternHandler, RefreshRoofHandler` |
| `ViewModels` | `TaperedEpsPattern2DViewModel, RoofCardViewModel, BoardSettingsViewModel, LayersViewModel, LayerOptionViewModel, NumericFieldViewModel, ChoiceOption<T>, MetricTileViewModel, SummaryRowViewModel, LogViewModel, LogEntryViewModel, PreviewViewModel` |
| `Views` | `TaperedEpsPattern2DWindow` (XAML), `PatternPreviewCanvas` |
| root | `TaperedEpsPattern2DToolInfo` |
