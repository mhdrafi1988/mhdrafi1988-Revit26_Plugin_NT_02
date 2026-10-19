# Roof Name Builder — V002

Bulk rename roof **Types** (type name) or **Instances** (a text parameter, `Mark`
by default), building each name as `Base` + `_CODEvalue` for every ticked code
(INS, SLP, MAT, … — configurable). Values are read from each roof's parameters.

Namespace: `Revit26_Plugin.RoofNameBuilder.V002`
Command:   `Revit26_Plugin.RoofNameBuilder.V002.Commands.RoofNameBuilderCommand`

## Install

1. Copy this folder to `Menu/RoofTools/RoofNameBuilder_V002/`.
2. Make the three edits in `RIBBON_WIRING_SNIPPET.cs.txt`: ribbon button,
   `ToolCatalog` entry, `.csproj` icon lines (+ retire V001 if it's in the repo).
3. Add the `CHANGELOG.md` entry from `CHANGELOG_ENTRY.md`.

No NuGet or Shared/ changes are needed beyond the one `ToolCatalog` line.

## Why a new version (not an in-place fix)

V001 was scaffolded without access to the repo, against guessed `Shared/`
names. Checked against the real files it **does not compile**:

| V001 | Real API |
|---|---|
| `SettingsService.Load<T>(key)` / `Save(key, s)` | `SettingsService<T>.Load(key)` / `.Save(key, s)` |
| `new LogEntry { Level, Message, Timestamp }` | `new LogEntry(level, message)` (read-only) |
| `ElementId.IntegerValue` | `ElementId.Value` (Revit 2026) |
| `IList<ElementId> = Selection.GetElementIds()` | returns `ICollection<ElementId>` |
| `ICollectionView` without `using System.ComponentModel` | — |

Its XAML would also have failed at load: converters declared beside
`MergedDictionaries` without an explicit `<ResourceDictionary>`, the
`;assembly=` xmlns that `Converters.cs` says not to use, `ChipToggle`
(a RadioButton style) applied to a ToggleButton, and a non-existent
`BrushLogBackground`.

On top of those fixes, V002 changes behaviour (hence the bump, per
`TOOL_VERSIONING.md`):

- **Existing codes are recognised.** A name like `Roof_INS50_SLP2` loads as
  base `Roof` with INS and SLP ticked. A re-run updates codes instead of
  appending them again (V001 would build `Roof_INS50_SLP2_INS50`). If a
  parameter has changed since the last rename, the row shows as *Rename*.
  Turn this off with *Recognise codes already in names*.
- **The grid is no longer empty on open.** V001 dropped every row that
  wasn't already pending, which on first load was every row. *Hide
  up-to-date* is now a live filter that hides rows that already carry codes
  and are current.
- **One tick + value column per code** in the grid. V001 had no code
  columns, so codes could only be ticked in bulk and never unticked.
- **Scope / Mode chips work.** `EnumToBoolConverter` is one-way, so they now
  go through `SetScope` / `SetMode` commands.
- **Better conflict checks.** Only rows you're actually renaming are flagged,
  and blank Marks are ignored (V001 blocked Run on any project with two
  unmarked roofs). In Types mode a new name is also checked against roof
  types outside the current scope. Illegal type-name characters
  (`\ : { } [ ] | ; < > ? `` ` `` ~`) are flagged and also block Run.
- **Values come in without unit symbols.** Lengths and slopes are read as
  plain numbers in project units (`50`, not `50 mm`). Each code's parameter
  is looked up on the instance first, then on its type.
- **Hardening.** Commands and the event handler run through `ToolGuard`. The
  rename targets the document the window was opened on, even if you've
  switched documents since. The window can't get stuck in *running*. Revit
  warnings (e.g. duplicate Mark) are logged per row instead of interrupting
  the batch. A refused `ExternalEvent` request is reported.
- **Code Definitions are editable.** You can add or delete codes and set the
  separator and the instance target parameter, then click *Apply & reload*.
  Codes must be unique and use letters and digits only.
- **Shell and log standards.** Uses `ToolViewModelBase` (log + Copy All /
  Copy Selected / Clear). The log is dark and collapsed by default. Export
  Log uses `LogExportService` with a folder picker. Scope, mode and filter
  state are saved on close and after each run.

Kept from V001 (per the original discussion): Instances mode writes `Mark`;
duplicate check is case-insensitive; Run is blocked while any conflict
exists; linked-model roofs aren't shown; CSV import/export and presets are
still not built.

## Known limits

- Swapping two type names in one run (A→B, B→A) fails for the first row,
  because the other name still exists at that moment. Run it in two passes.
- A code without a prefix (MAT) is only recognised in an existing name when
  it equals the parameter's current value.
- In Instances mode, two roofs that would get the same Mark block Run (as in
  V001). Give them different base names.
