# Wet Area Waterproofing V001 — Design

Status: awaiting owner approval · Preview: https://claude.ai/artifact/AxccNteVMVibgsyeXAjEiq
(`Claude/Previews/WetAreaWaterproofing_V001_Window.html`)

## Intent
One tool that takes a model from "rooms" to "printable waterproofing package": find wet rooms, model a
waterproofing layer on each slab, cut detail references at the places that leak (doors, drains, corners,
fixtures), lay everything out on sheets and hand back a print set plus an Excel report to audit the run.

## Goals
- Find rooms in host + loaded links; flag wet rooms by editable name keywords; user ticks the final list.
- Create a roof (user-picked type) from each room boundary on top of the slab under the room.
- Plans and up to 6 reference sections per room, pointing to existing or new drafting views.
- Sheets + View/Sheet Set. Excel report of everything created, repaired, skipped or failed.

## Non-goals (V001)
- Manual boundary sketching (owner: not required).
- Comparing against a previous run's report.
- New shared parameters (write-back uses Comments).

## Anti-goals
- Silently producing wrong geometry: every repaired/approximate outline is flagged in grid, dialog, log and Excel.
- Committing partial work after an error (per-step transactions, roll back on failure).

## Decisions (owner)
| Topic | Decision |
|---|---|
| Room source | Host + linked rooms; Source column |
| Wet detection | Editable name keywords (toilet, wc, bath, shower, kitchen, pantry, laundry, wash, balcony) |
| Roof height | Slab found under room → top face + offset (0 mm); no slab → room level + offset, warned |
| Roof type | Picked in UI, remembered; Comments = run ID + room no. + name |
| Room grid | Sort (multi-column), per-column filters (tick list / contains / range), global search, group by, **level filter buttons** (click toggle, Ctrl+click solo, counts), column show/hide remembered, tick/untick visible, invert, status bar |
| Section targets | Up to 6 per room: Full room H, Full room V, Door, Drain, Wall corner, WC/closet, Shower/bath, Wall–floor. Per target: **One (nearest centre)** or **All matches**; cap 6, higher rows win, overflow logged |
| Drafting views | **One view for all** or **one view per target**; each existing or created new |
| Boundary repair | Chain: other boundary location → clean → merge collinear → snap ≤ 5 mm → bridge ≤ 50 mm → remove overlaps/spikes (Clipper2) → arcs to lines → room 3D bottom face → skip failing inner loop → approximate (hull/bbox) |
| Approximate fallback | Included, **off by default** (my call) |
| Area check | ±2 % of room area. Outside → roof still created, user **notified** (grid red status, warning bar with "Show these rooms", result dialog, log, Excel) (my call) |
| Grouping | Rooms on the **same level** (host and links together) within 1000 mm share one plan; Off / Auto / Marked only; Group column; merge/split before run |
| Scale | Plans and sections default 1:100, changeable |
| Title blocks | Ordered list, **A0 first = default**; use first-in-list or smallest-that-fits; margins + title strip editable |
| Doesn't fit | Both options: extra sheet (WP-101a) or shrink scale |
| Sheets | One sheet per room/group, or packed (reuse `SectionsToSheet_V001` layout) |
| Print set | "Waterproofing - <date>"; existing name → in-window prompt (overwrite / suffix) |
| Excel | ClosedXML, one workbook: Summary, Rooms, Boundary repair (every attempt), Sections, Sheets, Issues; skipped rooms listed with reason; Group + Scale columns; header filters, frozen row, status colours; Phase + Workset columns |
| Excel saving | Auto-save after run (setting, on) + "Export now"; folder `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\Reports`, changeable; `WP_Report_<Model>_<yyyyMMdd_HHmm>.xlsx` (my call) |

## My calls (owner said "go with your suggestions")
- Ribbon: **Sheet** panel, new push button "Wet Area Waterproofing" (README + RIBBON_AND_ICONS.md updated).
- Linked rooms: slab searched in host first, then in the same link; link transform applied.
- Build order: **part by part** — (A) rooms grid + boundary repair + roofs + Excel, (B) plans + section targets + drafting views, (C) grouping + sheets + fit + print set. Each part builds, gets a preview update and is testable in Revit.

## Constraints (CLAUDE.md)
- Folder `Menu/Sheet/WetAreaWaterproofing_V001/{Commands,Core,UI,Infrastructure}`; namespace
  `Revit26_Plugin_Revised.WetAreaWaterproofing.V001`; view model `ViewModel_WetAreaWaterproofing`.
- `ToolCatalog` entry 1.0.0 / V001; plugin 7.8.0 → 7.9.0; CHANGELOG.
- Entry points via `ToolGuard.RunCommand` / `RunHandler`; model edits through ExternalEvent in `Transaction`/`TransactionGroup`.
- Worksharing: skip elements checked out by others (`WorksharingUtils.GetCheckoutStatus`), report.
- Settings JSON under `%AppData%\Revit26_Plugin_Revised\WetAreaWaterproofing\settings.json` with a version field.
- Build: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true`, 0 errors.

## Left to the builder
- Exact fixture detection categories (Plumbing Fixtures, Pipe Accessories) and keyword defaults.
- Reference-section creation API path (`ViewSection.CreateReferenceSection`) and drafting-view naming collisions.
- Pure geometry (repair chain, grouping, fit/packing) kept Revit-free where possible so it can get unit tests under `Tests/`.
