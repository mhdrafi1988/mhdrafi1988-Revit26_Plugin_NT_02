# Section Roof Detail Lines (V001)

Tool version **1.2.0** · namespace `Revit26_Plugin_Revised.SectionRoofDetailLines.V001` ·
ribbon **Rafi_Pro_Docs › Detail Lines › Section Lines**

Lists what a sheet's sections show — host model and every loaded Revit link — then traces the cut
profile of the roofs you tick and draws a line on the walls beside them, in the sections themselves.

## Why the lines go in the sections, not on the sheet

A detail line in Revit belongs to one view. Opening the sheet is only how the tool finds which
sections to draw in; each line is created in its own section view, which is also where Revit will
show and print it.

## How a section's contents are read

| Source | How it is read | What that catches |
| --- | --- | --- |
| Host model | `FilteredElementCollector(doc, view.Id)` — Revit's own answer to "what is visible here" | Crop, far clip, visibility/graphics, view filters, anything hidden by hand |
| Each loaded link | The section's crop box brought into the link's coordinates, then a bounding-box test per element (`SectionFrame.MayIntersect`) | Crop and far clip — which is what decides whether a roof or wall is cut |

Revit offers no view-visibility query for a linked document, so the link test is geometric and
deliberately generous: an element the view does not in fact show may be listed, and can be unticked,
rather than a roof the user *can* see being dropped in silence. A link set to "By linked view"
instead of "By host view" is where the two differ most.

## How the cut profile is found

The view's plane — through `View.Origin`, normal `View.ViewDirection` — is both the plane the model
is cut on and the plane a detail curve has to lie in. `BooleanOperationsUtils.CutWithHalfSpace`
cuts the element's solid on it; the face that leaves coplanar with the plane is the cut, and
`Face.GetEdgesAsCurveLoops` gives its edge loops — the outer loop and any opening the section passes
through — already in the right plane, with no projection needed.

An element the plane misses produces no such face, so an element the section *shows* but does not
*cut* yields nothing and is reported as having nothing to trace.

## What is drawn

- **Roof** — the edges of the cut profile on the sides you pick: **Top**, **Bottom**, **Left**,
  **Right**, all four by default, which is the whole profile. Straight edges are trimmed to the
  crop; an arc is kept whole, since Revit does not draw the part outside the crop anyway.
- **Wall** — one vertical line per chosen face of the wall's cut profile, at that face's own
  position across the view, over the wall's own height in the section held inside the crop. Only
  for a wall whose cut profile comes within the adjacency tolerance (default 50 mm) of a ticked
  roof's. **Wall face to draw on** picks which: **Nearer the roof** (the default, and what 1.0.0
  did), **Both faces**, **Left** or **Right**.

### How a side is decided

Not by where the edge sits in the profile — that breaks on a sloped or stepped roof — but by where
the material is. For each edge, a 0.3 mm step either way perpendicular to it is projected onto the
cut face (`Face.Project` then `Face.IsInside`); whichever probe lands on the face is the solid side,
so the other way is *out*. That outward direction, resolved against the view's up and right
(`cos 45°` splits top/bottom from left/right), gives the side.

Two consequences worth knowing:

- The underside of a roof reads as **Bottom** whether the roof is flat, sloped or stepped.
- The head of an opening the section passes through also reads as **Bottom**, because that surface
  is what you see standing under it. Its sill reads as **Top**.

Where both probes agree — at a corner, or on an edge shorter than the probe — the fallback compares
the edge with the middle of the cut face, which is right for the simple profiles that is the only
place it happens.

## Scope of a run

Every section on the active sheet is ticked, capped at **10** (`SectionRoofLinesSettings.MaxSectionsPerRun`).
A section open on its own, not yet on a sheet, is offered by itself. A section owned by another user,
or changed in central since the last reload, cannot be ticked and says why.

## Search and select

Every list and picker you choose from can be searched:

| Where | How it searches | What selecting does |
| --- | --- | --- |
| **Sections** (left) | Search box above the list: section name or detail number, anywhere in the text, any case | **Tick all** / **Untick all** act on the sections the search shows; hidden ones keep their tick and are still drawn |
| **Elements In These Sections** (middle) | Search box: source, category or type name | **Tick roofs & walls** ticks the roof and wall types the search shows |
| **Roof line style**, **Wall line style** (right) | Type in the picker: the drop-down narrows to the styles containing the text; "default" finds the view's default | Pick from the narrowed list. Text that matches no style is not kept: the picker goes back to the current choice when it closes |

The pickers are `ComboBox`es with `UI/Behaviors/FilterComboBehavior`. Each has its own
`ListCollectionView` over the one style list, so filtering one does not filter the other, and binds
`SelectedItem` to a property that ignores the `null` WPF sends while the choice is filtered out.

## Safety

- One `Transaction` inside one `TransactionGroup`, assimilated: a single Ctrl+Z undoes the whole run.
- An element whose geometry cannot be read, or that the section does not cut, is reported and
  skipped; the rest still go in.
- A cancel (Esc, or the Cancel button in the status strip) rolls the group back — no half-run is
  left in the model.
- Both entry points go through `ToolGuard.RunCommand` / `ToolGuard.RunHandler`.
- No Revit event is subscribed from the WPF thread, so nothing has to be unsubscribed on close —
  the API-context trap in CLAUDE.md's Robustness §6 does not arise here.

## Layout

```
Commands/     SectionRoofDetailLinesCommand.cs   entry point, view checks, opens the window
Core/Models/  Enums, ElementKey/TypeKey, SectionRoofLinesSettings, SectionSnapshot
Core/Services SectionFrame          the view's plane and visible box
              SheetSectionCollector which sections a run covers
              VisibleElementCollector host + linked elements a section shows
              CutProfileService     solids, and the profile the plane cuts through them
              SectionLineBuilder    roof curves and wall-face lines
              SectionDetailLineWriter detail curves, line styles, duplicate skip
              ModelReader           one snapshot for the window
Core/Run/     SectionLineRunner     the transaction, per-section orchestration, the report
Infrastructure SectionRoofLinesHandler  the one ExternalEvent
UI/           ViewModel_SectionRoofDetailLines (+ .Run), tree nodes, row, LineStyleOption,
              converters, Behaviors/FilterComboBehavior (type-to-filter pickers), window
```

## Settings

`%AppData%\Revit26_Plugin_Revised\SectionRoofDetailLines\settings.json`, with a `Version` field so a
later release can migrate an older file. `Normalize()` brings any out-of-range or missing value back
to a usable default, so a hand-edited file never stops the tool from opening.

## UI preview

`Claude/Previews/SectionRoofDetailLines_V001_Window.html`
