# Merge Roofs — Design (V001)

## Goal
Select two or more footprint roofs and create ONE new footprint roof from their combined outline
(outer boundary + interior loops). Originals are kept.

## Scope
- Footprint roofs only (no extrusion roofs). Minimum 2 roofs.
- Skip/report roofs that are read-only or checked out by another user (`WorksharingUtils.GetCheckoutStatus`).
- Location: `Menu/RoofTools/Modelling/MergeRoofs_V001/{Commands,Core,UI,Infrastructure}`.
- Namespace `Revit26_Plugin_Revised.MergeRoofs.V001`; new `ToolCatalog` entry (1.0.0); ribbon button
  (+ RIBBON_AND_ICONS.md and README Ribbon section); CHANGELOG; plugin version bump; HTML preview
  `Claude/Previews/MergeRoofs_V001_Window.html` published as an Artifact.
- Entry point via `ToolGuard.RunCommand`.

## Flow
1. Take pre-selected roofs, else prompt for a pick.
2. Read each roof's footprint `CurveArrays` (outer + holes) with per-edge slope flag/angle.
3. WPF window: roof list, gap tolerance (default 5 mm, adjustable), checkbox
   "Place connecting lines across gaps", **Check** button.
4. Analyse, using the tolerance: overlap, intersection, gap (snapped closed), not joined
   (gap > tolerance or point contact only).
5. Merge: union of footprints -> closed loops (one outer, rest holes). If the checkbox is ticked,
   draw detail lines across snapped gaps in the current view.
6. Create the new footprint roof in one `Transaction`: type + level of the first selected roof;
   each edge keeps the slope of the source roof edge it came from. Originals untouched.
7. If the roof cannot be created (open/invalid loop, or Revit rejects the sketch):
   - Ask: "Place the profile as detail lines in the current view?" -> if yes, draw them.
   - Ask separately: "Mark issue areas with circles?" -> if yes, draw detail circles.
   - Nothing else in the model changes; transaction rolled back.

## Geometry
Pure `Core` class (no Revit types) for testability: Clipper2 polygon union (NuGet). Arcs are
flattened for the union only; straight edges stay straight. Gap snapping uses the tolerance.
Arc handling: after the union, runs of segments that lie on an original arc are re-fitted back into
that arc and the roof is created ONCE with arcs. If that attempt fails (fit or Revit sketch error),
retry once with the flattened segments. The result window states which was used.

## Errors
All failures shown in the window or logged via `ToolGuard.Report`. No silent failures.

## Tests
`Tests/MergeRoofs.Tests`: overlap, shared edge, point contact, gap inside/outside tolerance,
holes, disjoint roofs.

## Out of scope
Extrusion roofs, deleting originals, linked-model roofs.
