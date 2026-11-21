# Section Roof Detail Lines (V001)

Tool version **1.0.0** · namespace `Revit26_Plugin_Revised.SectionRoofDetailLines.V001` ·
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

- **Roof** — the whole cut profile. Straight edges are trimmed to the crop; an arc is kept whole,
  since Revit does not draw the part outside the crop anyway.
- **Wall** — one vertical line per face of the wall's cut profile, at that face's own position
  across the view, over the wall's own height in the section held inside the crop. Only for a wall
  whose cut profile comes within the adjacency tolerance (default 50 mm) of a ticked roof's.
  Off by default, **Line on both faces** draws the far face too; otherwise only the face nearer the
  roof gets a line.

## Scope of a run

Every section on the active sheet is ticked, capped at **10** (`SectionRoofLinesSettings.MaxSectionsPerRun`).
A section open on its own, not yet on a sheet, is offered by itself. A section owned by another user,
or changed in central since the last reload, cannot be ticked and says why.

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
UI/           ViewModel_SectionRoofDetailLines (+ .Run), tree nodes, row, converters, window
```

## Settings

`%AppData%\Revit26_Plugin_Revised\SectionRoofDetailLines\settings.json`, with a `Version` field so a
later release can migrate an older file. `Normalize()` brings any out-of-range or missing value back
to a usable default, so a hand-edited file never stops the tool from opening.

## UI preview

`Claude/Previews/SectionRoofDetailLines_V001_Window.html`
