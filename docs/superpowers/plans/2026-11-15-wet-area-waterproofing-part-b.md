# Wet Area Waterproofing — Part B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add plans and detail references to Wet Area Waterproofing V001: a cropped plan per room and up to 6 reference sections per room pointing to drafting views.

**Architecture:** Same split as Part A. Revit-free core decides *which* sections go *where* (`Core/Sections`) and is unit-tested; `Infrastructure/` collects doors/drains/fixtures, creates plans, drafting views and reference-section markers. Created views and markers are tracked with an Extensible Storage tag (views have no Comments).

**Tech Stack:** as Part A. Reference sections via `ViewSection.CreateReferenceSection` (returns void → marker found by diffing `OST_Viewers` ids, as in `RefSectionHeadPlacer_V015`).

**Spec:** `docs/superpowers/specs/2026-11-14-wet-area-waterproofing-design.md` (rows: Section targets, Target sources, Drafting views, Re-run, Undo).

## Global Constraints
- Tool version 1.0.0 → **1.1.0**; plugin 7.11.0 → **7.12.0**; CHANGELOG; preview updated and republished.
- Targets, in default order: Full room – horizontal (on), Full room – vertical (on), Door / threshold (on), Floor drain (on), Wall corner (off), WC / closet (off), Shower / bath (off), Wall–floor (off). Max **6** sections per room; rows higher in the list win; overflow and missing targets are logged, never invented.
- Per target: **One** (match nearest the room centre) or **All matches**; cut length mm (defaults: full room = room + 300 each side, door 1500, drain 1200, corner 1000, WC 1200, shower 1200, wall–floor 1000).
- Drafting views: one for all targets (default name `WP Detail - Typical Wet Area`) or one per target; an existing view by name, or created when missing (scale 1:100 default).
- Keywords (editable): drains `drain, fd, floor drain`; WC `wc, closet, toilet`; shower `shower, bath, tray`.
- Target sources: host + every loaded link × categories Doors, Plumbing Fixtures, Pipe Accessories; each tickable; default all on.
- Plan: floor plan type and view template picked in UI; crop = outline bounding box + margin (default 500 mm); scale default 1:100; name pattern `WP - {Level} - {Number} {Name}`.
- Run steps (tick boxes): Roofs, Plans, Reference sections. Sections need a plan: an existing tool-made plan is reused, otherwise one is created.
- Re-run: plan kept (found by tag), crop refreshed; the room's tool-made section markers are deleted and placed again. Remove a run deletes tagged plans, markers and drafting views *the tool created* for that run.
- Extensible Storage schema `WetAreaWaterproofing_Tag_V1` (new GUID, never changed later): fields `RunId`, `RoomKey`, `Kind` (Plan / Section / Drafting).
- Excel: new sheet **Sections** before Issues; Rooms sheet gains columns at the end: `Plan view Id`, `Plan view`, `Sections`.
- Not in Part B: grouping, sheets, title blocks, fit check, print set.

## Review Focus
1. Door on a room's boundary (not inside the outline) is still found for that room, and a door between two wet rooms is cut for both.
2. More matches than slots → exactly 6 sections, higher rows win, the rest listed as skipped.
3. A target with no match → skipped with a message; other targets still placed.
4. L-shaped room: full-room cut lines pass through the room (through a point inside it), not through the empty corner.
5. View-name clashes: a plan or drafting view name that already exists on a view the tool did not make → suffix ` (2)`, never an exception.

## Tasks
1. **Settings + enums** (`Core/Sections/SectionTargets.cs`, `WetAreaSettings`): target list, keywords, plan settings, run steps, sources-off list; `Normalize` restores missing targets and clamps lengths. Tests: defaults, normalize.
2. **Item classification and room matching** (`Core/Sections/ItemClassifier.cs`, `RoomItemMatcher.cs`, `Polyline2D.Nearest`): category + names → kind; items inside the outline, doors within 400 mm of it, Z within level −500 … +3000 mm. Tests incl. Review Focus 1.
3. **Section planner** (`Core/Sections/SectionPlanner.cs`): cut lines per target kind, one/all, cap 6, skipped list, names. Tests incl. Review Focus 2–4.
4. **View names** (`Core/Sections/ViewNames.cs`): pattern fill, illegal characters, unique suffix. Tests incl. Review Focus 5.
5. **Report**: Sections sheet + Rooms columns. Tests updated/added.
6. **Revit**: `ElementTag` (schema), `TargetCollector`, `ViewCreator` (plan, drafting, markers), session run steps, `RunRemover` covers tagged elements; snapshot carries plan types, templates, drafting views, sources tree, per-room counts.
7. **UI**: cards "3 · Plans" and "4 · Section targets" (table with order buttons, sources tree, drafting mode), run-step ticks, grid columns Doors / Drains / Fixtures / Sections, result lines.
8. **Docs**: versions, CHANGELOG, README, preview, PR text. Final whole-branch review.
