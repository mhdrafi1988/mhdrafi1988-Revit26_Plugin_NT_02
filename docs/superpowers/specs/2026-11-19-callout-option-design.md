# Per-target callout option for Wet Area Waterproofing

**Date**: 2026-11-19
**Tool**: Wet Area Waterproofing V001 → version 1.2.0

## Intent

Reference sections point to empty drafting views — the user draws the detail manually.
Callouts crop into the real model, showing geometry at doors, drains, corners instantly.
Each section target row picks its own mode so standard details stay as reference sections
while element-specific cuts become callouts.

## Goals

- Each target in the section targets table can be set to Reference Section or Callout.
- Callouts use `ViewSection.CreateCallout` and create a detail view cropped to the cut area.
- A dropdown lets the user pick which detail view family type the callouts use.
- Old settings files load unchanged (backward compatible).

## Non-goals

- Not changing the section geometry (SectionPlanner stays the same).
- Not adding callout-specific view templates (future work).
- Not changing roof or plan creation.

## Anti-goals

- Breaking existing settings files or ribbon buttons.
- Forcing all sections to be callouts when the user has standard detail drafting views.

## Constraints

- Revit API: `ViewSection.CreateCallout(doc, parentViewId, viewFamilyTypeId, pt1, pt2)` takes
  two XYZ points that define a rectangle on the parent view. The reference section API takes
  head/tail on a line. The callout points define a crop box, not a cut line.
- The callout's crop rectangle must be derived from the same head/tail geometry the planner
  already computes, turned into a bounding box with a margin.

## Approach

**Chosen: per-target mode column + detail view type dropdown.**

Rejected: global toggle (loses the value of mixing modes per target).
Rejected: per-target with inline type picker (too complex for the grid UI).

## Changes

1. `Enums.cs` — add `SectionMode { ReferenceSection = 0, Callout = 1 }`
2. `SectionTargetSetting` — add `Mode` property (default ReferenceSection)
3. `SectionTargets.Normalize` — handle new field
4. `PlannedSection` — add `Mode` field
5. `SectionPlanner` — carry Mode from setting to planned section
6. `WetAreaSettings` — add `CalloutViewTypeName`, `CalloutScale`, `CalloutMarginMm`
7. `ViewCreator` — fork placement: ReferenceSection → existing path, Callout → CreateCallout
8. `ViewCreator.CheckReady` — validate detail view type when any target uses Callout
9. `RoomCollector/ModelSnapshot` — collect detail view type names
10. `RunOutcome` — add `CalloutsCreated`
11. ViewModel — expose detail view types, selected type, Mode column binding
12. XAML — ComboBox column for Mode, detail view type dropdown
13. Report — distinguish callouts from reference sections
14. Tests — update SectionPlannerTests
15. ToolCatalog — bump to 1.2.0
16. CHANGELOG + HTML preview — update

## Left to the builder

- Exact callout crop margin (propose 300 mm default, stored in settings).
- Callout view scale (propose same as plan scale by default, stored in settings).
- Callout naming pattern (propose reusing the section label + room number).
