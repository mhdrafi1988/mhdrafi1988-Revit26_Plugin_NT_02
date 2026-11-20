# Existing-sheet target for Sections To Sheet

**Date**: 2026-11-20
**Tool**: Sections To Sheet V001 → version 1.2.0 (plugin 7.28.0)
**Preview**: `Claude/Previews/SectionsToSheet_V001_Window.html` — https://claude.ai/artifact/HMYzMyc95SF4zyDxxZjHTQ

## Intent

Today the tool always creates a new sheet. The owner wants to put the collected sections on a
sheet that already exists, without hand-placing them.

## Goals

- Target Sheet: **New sheet** (today's behaviour, the default) or **Existing sheet**, picked from a list.
- Existing sheet, **Use free space**: nothing on the sheet moves. The new sections are laid out
  (Grid / Rows, same rules) inside the largest empty rectangle of the usable area.
- Existing sheet, **Re-arrange all views**: every viewport on the sheet is laid out again together
  with the new sections, existing views first (in their current reading order on the sheet), then
  the new sections in the tool's order. Schedules, text and every other item stay put and are kept clear of.
- Existing views keep their detail numbers; new sections continue from the highest numeric detail
  number on the sheet.
- Sections that don't fit go on extra new sheets (or are skipped, per the existing overflow setting).
- Same live preview, same "model changed since the preview" signature check, one Undo step.

## Non-goals

- No change to how sections are collected, measured or ordered.
- No moving of schedules, legends' contents, text or other non-viewport items.
- No change to new-sheet behaviour.

## Anti-goals

- Moving a view the user didn't ask to move (free-space mode moves nothing on the target).
- Overlapping existing items. Breaking old settings files.

## Approach

Chosen: extend the existing pipeline in place (owner approved). Rejected: reusing Sheet Auto
Rearrange for the re-arrange part (two layout rules, preview ≠ result); a separate tool
(duplication, against repo rules).

## Design

- **Models** (`Core/Models`, Revit-free): `TargetMode { NewSheet, ExistingSheet }`,
  `ExistingSheetMode { FreeSpace, Rearrange }`; `TargetSheetContent` (key, number, name, title block
  name, frame, lock reason, items) and `TargetItem` (rect, box centre, view key, name, scale, detail
  number, is-viewport, lock reason). `SheetChoice` list in the snapshot for the picker.
- **Settings** (version 2): `Target` (default NewSheet), `ExistingMode` (default FreeSpace),
  `TargetSheetNumber` (remembered). Version-1 files load with these defaults.
- **Free space** (`Core/Layout/FreeSpace.cs`, pure): largest empty axis-aligned rectangle of the
  area that clears every obstacle, obstacles grown by the smallest side-by-side / row gap.
  Ties: larger area, then higher, then further left.
- **Planner**: existing-sheet page 0 uses the target's frame and the free rectangle as its area
  (single page); leftovers go through today's multi-sheet layout on the chosen title block. In
  re-arrange mode, an existing view that does not fit blocks the run. Sections already on the
  target sheet are not moved again (free space: "stays where it is"; re-arrange: they are part
  of the sheet's views). Locked viewports stay put as obstacles, with a warning.
- **Revit side**: `TargetSheetReader` reads the sheet's title block extent, viewports
  (box ∪ title), schedules and other items' bounding boxes, and worksharing locks.
  `PlacementRunner` places onto the existing sheet, moves re-arranged viewports with
  `SetBoxCenter` (unpin → move → re-pin), numbers only the new viewports.
- **UI**: "New Sheet" card becomes "Target Sheet"; preview draws items that stay (grey), views that
  move (navy) and the free area (green dashed); button reads "Place on <number>"; the confirm card
  also opens when existing views will be moved.

## Left to the builder

Exact wording of hints and log lines, following the preview.
