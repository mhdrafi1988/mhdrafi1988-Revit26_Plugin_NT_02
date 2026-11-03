# AI Image-Generation Prompts — Ribbon Tool Icons

One monochrome icon prompt per live `PushButton` in the ribbon, after the cleanup pass that removed dead entries and added missing ones. Generated from the state of `Menu/00_Push_Button_Menu_items/Ribbon/*.cs` + `QuickAcces.cs` as wired up in `App.cs`.

> **Status (2026-11-02, plugin 6.31.0):** entries 1–64 were written for an older ribbon. Some button IDs and
> versions below no longer exist, and `QuickAcces.cs` is gone. The current layout is in
> `Claude/Revit26_Plugin_Md Files/Claude/RIBBON_AND_ICONS.md`. Entries 65–66 cover the Floor Slope panel added in 6.31.0;
> its icons were made in 6.31.1.
> Add a prompt here whenever a new button needs its own icon.

## Shared style brief (prepend to every prompt)

> Flat monochrome line-art icon for a Revit ribbon toolbar button, single dark-gray/black stroke (#2B2B2B) on a fully transparent background, 32x32px canvas, simple geometric silhouette, no gradients, no shadows, no text or letters, no color, centered composition with even padding, consistent 2px stroke weight, matches the visual weight of Autodesk Revit's native ribbon icon set.

Each entry below is: **Button ID** → suggested filename (32px PNG, matches the project's `<Name>_32.png` convention) → subject-only prompt (append to the shared style brief above).

Several tool families already have an unused, never-wired icon sitting in `Resources/Icons/` (see `Ribbon_Code_Notes.md` §1) — those are flagged with "♻ reuse/refresh" so the same file can be regenerated instead of adding a new asset.

---

## Dimensions (`DimensionsRibbon.cs` — DimMenu pulldown)

1. **Btn_DtlLine_07** → `DtlLineDim_V007_32.png` — a detail line with a dimension string automatically snapping to both endpoints, arrows pointing outward.
2. **Btn_DtlLine_08** → `DtlLineDim_V008_32.png` — same detail-line-with-dimension motif as V007 but with a small "+" badge in the corner to signal the newer engine.

## Floor Tools (`FloorRibbon.cs` — Create pulldown)

3. **Btn_FloorsAndRoofFromLinkedRoomsViaPlanViewV004** → `FloorsAndRoofFromLinkedRoomsViaPlanView_32.png` — a floor plan rectangle with a dashed linked-room outline inside it and an upward arrow turning the outline into a solid roof pitch symbol.
4. **Btn_FloorsAndRoofFromLinkedRooms_V011** → `FloorsAndRoofFromLinkedRooms_32.png` — two overlapping room polygons (one dashed/linked, one solid) merging into a single floor slab icon with a small roof ridge line on top.

## Manage (`ManageRibbon.cs`)

5. **Btn_AnnotationOverlapDetection_V002** → `AnnotationOverlapDetection_32.png` — two overlapping tag/label rectangles with a magnifying glass over the overlapping area and a small warning triangle.

## Roof Tools — Auto Slope (`RoofToolsRibbon.cs` — SlopeMenu pulldown)

6. **Btn_AutoSlopeByPoint_028** → `AutoSlopeByPoint_32.png` ♻ reuse/refresh — a roof plan with several dots (points) and diagonal slope-arrow hatching radiating from each dot toward a drain symbol.
7. **Btn_AutoSlopeByPointRPF_V003** → `AutoSlopeByPointRPF_32.png` — same point-and-slope-arrow motif as AutoSlopeByPoint, plus a small ridge-line chevron above the points to indicate ridge-aware pathing.
8. **Btn_AutoSlopeByDrain_V006** → `AutoSlopeByDrain_32.png` ♻ reuse/refresh — a roof drain circle-with-cross symbol at the center with converging slope arrows from the roof edges toward it.
9. **Btn_AutoSlopeByDrain_V007** → `AutoSlopeByDrain_32.png` ♻ reuse/refresh (same subject as V006; regenerate the same asset rather than a near-duplicate) — a roof drain circle-with-cross symbol at the center with converging slope arrows from the roof edges toward it, one arrow slightly bolder to suggest a refined path engine.

## Roof Tools — Shape Points (`RoofToolsRibbon.cs` — ShapepointMenu pulldown)

10. **Btn_InnerLoopDivider_V009** → `InnerLoopDivider_32.png` — a closed interior loop (small rounded rectangle) with evenly spaced tick-mark points dividing its perimeter.
11. **Btn_RoofLoopAnalyzerPDC_V005** → `RoofLoopAnalyzerPDC_32.png` — a closed roof-edge loop with a magnifying glass and small analysis nodes (dots connected by thin lines) inside it.
12. **Btn_OuterCurveDivider_V004** → `OuterCurveDivider_32.png` — an outer curved boundary line with evenly spaced tick-mark points placed along the curve.
13. **Btn_RoofDetailLineIntersect_V011** → `RoofDetailLineIntersect_32.png` — two detail lines crossing at an X with a solid dot marking the intersection point.
14. **Btn_InnerLoopsAndPerpendicular_V005** → `InnerLoopsAndPerpendicular_32.png` — an interior loop with one point on its edge and a short perpendicular tick line projecting outward from that point.
15. **Btn_VertexReducer_V007** → `RoofEdgeVertexReducer_32.png` — a jagged polyline with many vertices on the left simplifying into a smooth polyline with few vertices on the right, separated by a small arrow.

## Roof Tools — Line & Point Ridge Creators (`RoofToolsRibbon.cs` — LineAndPoint pulldown)

16. **Btn_RoofRidgeLines_V57** → `RoofRidgeLines_32.png` ♻ reuse/refresh — a hip-roof plan outline with a solid ridge line generated down the center and small point markers at the ridge ends.
17. **Btn_RoofRidgeLines_V56** → `RoofRidgeLines_V56_32.png` — same hip-roof-with-ridge-line subject as V57, drawn from point markers only (no shape outline emphasis).
18. **Btn_RoofRidgeLines_V60** → `RoofRidgeLines_V60_32.png` — a multi-shape roof outline (two joined rectangles) each generating its own ridge line, emphasizing the shape-based derivation.
19. **Btn_RoofRidgeLines_V62** → `RoofRidgeLines_V62_32.png` — same multi-shape roof outline as V60 with ridge lines, plus a small valley-line diagonal where the two shapes meet.
20. **Btn_RoofRidgeLines_V67** → `RoofRidgeLines_V67_32.png` — a complex multi-wing roof outline (three joined shapes) fully resolved with ridge and valley lines.
21. **Btn_RoofRidgeLines_V68** → `RoofRidgeLines_V68_32.png` — same complex multi-wing roof outline as V67 with a small checkmark badge to signal the most current/validated version.

## Roof Tools — Creaser / Slope Liner (`RoofToolsRibbon.cs` — SlopeLinerMenu pulldown)

22. **Btn_CreaserAdvCommand_V002_01** → `CreaserAdv_32.png` — a sloped roof cross-section with a single crease/fold line placed along the ridge, small triangular fold indicator.
23. **Btn_CreaserAdvCommand_V003_01** → `CreaserAdv_V003_32.png` — same roof crease-line subject as V002_01, with a drain-point dot feeding the crease line.
24. **Btn_CreaserAdvCommand_V004_00** → `CreaserAdv_V004_32.png` — same roof crease-line subject, now with two parallel crease lines to indicate multi-segment placement.
25. **Btn_CreaserAdvCommand_V005_00** → `CreaserAdv_V005_32.png` — roof crease lines with a small horizontal-filter tick mark (flat/horizontal segments highlighted differently).
26. **Btn_CreaserAdvCommand_V006_00** → `CreaserAdv_V006_32.png` — roof crease lines with a thin dashed path connecting several drain points (pathfinding motif).
27. **Btn_CreaserAdvCommand_V007_00** → `CreaserAdv_V007_32.png` — same dashed pathfinding crease-line subject as V006, drawn slightly bolder/cleaner to signal a refined pass.
28. **Btn_CreaserAdvCommand_V008_00** → `CreaserAdv_V008_32.png` — roof crease lines converging on a shared top face, small "shared face" hatch fill under the crease.
29. **Btn_CreaserAdvCommand_V009_00** → `CreaserAdv_V009_32.png` — the same shared-top-face crease-line subject as V008, with a small checkmark badge for the current/most-refactored version.

## Roof Tools — Tag (`RoofToolsRibbon.cs` — tagMenu pulldown)

30. **Btn_RoofTagCommand_V008** → `RoofTag_32.png` — a roof polygon outline with a leader line pointing to a small tag/label box.
31. **Btn_RoofTagCommand_V014** → `RoofTag_V014_32.png` — same roof-outline-with-tag-leader subject as V008, plus small vertex dots along the roof edge to show interior-loop awareness.
32. **Btn_RoofTagCommand_V015** → `RoofTag_V015_32.png` — same subject as V014, with the vertex dots reduced/simplified to indicate loop-reduction handling.
33. **Btn_RoofTagCommand_V016** → `RoofTag_V016_32.png` — same roof-outline-with-tag-leader subject, with a small checkmark badge for the current version.

## Roof Tools — Create (`RoofToolsRibbon.cs` — CreateMenu pulldown)

34. **Btn_RoofFromDetailLines.V007** → `RoofFromDetailLines_32.png` — a flat 2D closed detail-line polygon on the left transforming (via an arrow) into a pitched 3D roof solid on the right.

## Setup — Workset Manager (`SetupRibbon.cs` — setup pulldown)

35. **Btn_WorksetManager_11** → `WorksetManager_32.png` — three stacked horizontal bars (worksets) each linked by a small chain-link icon to an external linked-file box.
36. **Btn_WorksetManager_010** → `WorksetManager_V010_32.png` — same worksets-linked-to-file subject as V011, drawn with fewer bars to differentiate the version visually.
37. **Btn_WorksetManager_011** → `WorksetManager_V011_32.png` — three stacked horizontal bars (worksets) linked by a chain-link icon to an external linked-file box, one bar highlighted.
38. **Btn_WorksetRenamer_V003** → `WorksetRenamer_32.png` — a single workset bar with a small pencil/edit icon overlapping its label area.
39. **Btn_WorksetManager_V012_New** → `WorksetManager_V012_32.png` — the worksets-linked-to-file subject with a small checkmark badge for the current/refactored version.

## Setup — Batch Link / DWG (`SetupRibbon.cs` — Linker pulldown)

40. **BatchLinkDwgCommand** → `BatchDwgFamilyLinker_32.png` ♻ reuse/refresh (`Resources/Icons/Linker_32.png` family) — a stack of small DWG file rectangles all connected by lines converging into a single family/host document icon.
41. **DwgSymbolicConverter_V01** → `DwgSymbolicConverter_32.png` — a DWG file icon on the left with an arrow converting it into Revit detail-line strokes on the right.
42. **DwgSymbolicConverter_V03** → `DwgSymbolicConverter_V03_32.png` — same DWG-to-lines conversion subject as V01, arrow drawn as a gear/process symbol to indicate the automated pipeline.
43. **DwgSymbolicConverter_V04** → `DwgToLines_32.png` — same DWG-to-lines conversion subject, output lines drawn cleaner/simplified for the newer engine.
44. **DwgToDetailLines_Project_V001** → `DwgToDetailLinesProject_32.png` — a DWG file icon converting into detail lines, with a small project/folder badge indicating project-wide scope.
45. **DwgToDetailLines_Project_V010** → `DwgToDetailLinesProject_V010_32.png` — same project-scope DWG-to-detail-lines subject as V001, with a small checkmark badge for the current pipeline.
46. **Btn_DwgToDetailLines_V002** → `DwgToDetailLines_V002_32.png` — a DWG file icon converting into detail lines (single-view scope, no folder badge).
47. **Btn_DwgToLines_V005_New** → `DwgToLines_V005_32.png` — DWG file converting into clean detail lines, small checkmark badge for the refactored version.
48. **Btn_DwgToDetailLines_V011_New** → `DwgToDetailLines_V011_32.png` — DWG file converting into detail lines with a project-folder badge and a checkmark badge together, for the current refactored pipeline.

## Sheet — Create (`SheetToolsRibbon.cs` — SheetCreate pulldown)

49. **Btn_PlanFromScopeBox.V003** → `PlanFromScopeBox_32.png` — a scope-box rectangle (dashed corners) generating a floor-plan view rectangle below it, connected by a small arrow.
50. **Btn_SmartViewToSheetPlacer.V221** → `SmartViewToSheetPlacer_32.png` ♻ reuse/refresh — a small view-viewport rectangle sliding onto a larger sheet-border rectangle, snapping into a titleblock grid cell.

## Sheet — Place (`SheetToolsRibbon.cs` — SheetPlace pulldown)

51. **Btn_SheetAutoRearrange.V022** → `SheetAutoRearrange_32.png` ♻ reuse/refresh — a grid of small sheet-border rectangles being reordered, shown with two rectangles mid-swap connected by curved arrows.
52. **Btn_SheetAutoRearrange.V023** → `SheetAutoRearrange_V023_32.png` — same sheet-reordering subject as V022, grid drawn slightly denser/cleaner with a checkmark badge for the current version.

## View Tools — Create (`ViewToolsRibbon.cs` — ViewCreate pulldown)

53. **Btn_ Sections From Detail Lines V11** → `CreateSectionsFromDetailLines_32.png` — a closed detail-line polygon with a section-cut symbol (bold arrow through a dashed line) generated along one of its edges.

## View Tools — Place (`ViewToolsRibbon.cs` — ViewPlace pulldown)

54. **Btn_AutoPlaceSectionsCommand_V321_01** → `APUS_32.png` — a floor plan outline with several section-marker arrows (flag-and-line symbols) auto-placed around its perimeter.
55. **Btn_AutoPlaceSectionsCommand_V322_01** → `APUS_V322_32.png` — same auto-placed section-marker subject as V321_01, one marker highlighted with a checkmark badge for the current version.
56. **Btn_CalloutToSectionViewPlacement_V019** → `CalloutCOP_32.png` — a dashed callout bubble outline transforming into a placed section-marker arrow via a small connecting arrow.
57. **Btn_RefSectionHeadPlacerCommand V013** → `RefSectionHeadPlacer_32.png` — a section-line with a circular reference-head bubble being placed/snapped at its end point.
58. **Btn_SectionViewAutoTagger.V003** → `SectionViewAutoTagger_32.png` ♻ reuse/refresh — a section-marker arrow with a small tag/label box automatically attaching to it via a leader line.
59. **Btn_SectionViewAutoTagger.V004** → `SectionViewAutoTagger_V004_32.png` — same section-marker-with-tag subject as V003, with a checkmark badge for the current version.

## View Tools — Rename (`ViewToolsRibbon.cs` — ViewRename pulldown)

60. **Btn_BubbleAutoRenumber_V006** → `BubbleAutoRenumber_32.png` — a row of circular bubble numbers (1, 2, 3 as plain dots to stay text-free) being reflowed in sequence with a curved reorder arrow.
61. **Btn_SectionAutoRenamer_V024** → `SectionAutoRenamer_32.png` ♻ reuse/refresh — a section-marker arrow with a small pencil/edit icon over its label area and a refresh/cycle arrow beside it.
62. **Btn_SectionManager_V008** → `SectionManager_32.png` — a small list/table icon (three horizontal rows) with a section-marker arrow glyph in the first row, representing a management panel.
63. **Btn_SectionManagerRefactor_V002** → `SectionManagerRefactor_32.png` — same list-panel-with-section-marker subject as SectionManager, with a small gear badge to indicate the refactored engine.
64. **Btn_ViewAutoRenamer_V003** → `ViewAutoRenamer_32.png` ♻ reuse/refresh — a generic view-viewport rectangle with a pencil/edit icon over its label area and a refresh/cycle arrow beside it.

## Floor Slope (`FloorRibbon.cs` — `FloorToolsRibbon.BuildSlopePanel`, plugin 6.31.0)

**Done in 6.31.1.** These icons were drawn in code rather than generated from the prompts, so they match the Floor Tools style
exactly. `make_floor_slope_icons.py` (in this folder) writes them to `Resources/Icons/FloorTools/`. To change one, edit the
script and run it again:

```bash
python3 "Claude/LOGO PROMPT FOLDER/make_floor_slope_icons.py" Resources/Icons/FloorTools
```

Style: Floor Tools orange square (#E8752C) with white line art, the drafting symbol for floor falls in plan.

65. **Btn_FloorSlopeByDrain_V001** → `FloorSlopeByDrain_16.png` / `FloorSlopeByDrain_32.png` — the slab outline in plan with fall lines from its four corners into a central drain (ring and dot). At 32 px the fall lines are arrows; at 16 px they are plain lines into a solid drain dot.
66. **Btn_FloorSlopeByPoint_V001** → `FloorSlopeByPoint_16.png` / `FloorSlopeByPoint_32.png` — the same slab outline with two picked points (ring and dot), each taking the falls from its half of the slab, and the ridge line between them (32 px only).
