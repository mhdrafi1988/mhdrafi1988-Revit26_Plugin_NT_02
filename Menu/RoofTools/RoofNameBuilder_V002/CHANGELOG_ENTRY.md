## 2026-10-14 — Roof Name Builder V002

- RoofNameBuilder bumped V001 → V002 (new folder + namespace
  `Revit26_Plugin_Revised.RoofNameBuilder.V002`). V001 kept on disk; it never
  compiled against the repo (guessed `Shared/` API: `SettingsService.Load<T>`,
  `LogEntry` initializer, `ElementId.IntegerValue`) — retire it with
  `<Compile Remove>` + `<Page Remove>` if it was added to the project.
- Behaviour change: names that already carry codes (`Roof_INS50_SLP2`) are
  parsed back into base name + ticked codes on load, so re-runs update codes
  instead of appending duplicates, and stale codes show as pending. Toggle:
  `RoofNameBuilderSettings.ParseExistingCodes` (default on).
- "Skip already compliant" is now a live *Hide up-to-date* filter (V001
  removed every row at load). One tick + value column per code in the grid;
  bulk Tick / Untick for selected rows; Code Definitions add / delete / Apply.
- Conflict check flags only pending rows, ignores blank names, and (Types
  mode) includes roof types outside the scope; illegal type-name characters
  block Run.
- On the shared stack: `ToolViewModelBase`, `SettingsService<T>`, `ToolGuard`
  (command + handler), `LogExportService`; ToolWindowShell status strip
  indeterminate on `IsRunning`; dark collapsed log with Copy All / Copy
  Selected / Clear.
- Handler renames in the document the window was opened on, always reports
  completion, logs (and dismisses) per-row Revit warnings.
- Ribbon: `Btn_RoofNameBuilder_V002` →
  `Revit26_Plugin_Revised.RoofNameBuilder.V002.Commands.RoofNameBuilderCommand`.
  `ToolCatalog.RoofNameBuilder` = 2.0.0 / V002.
