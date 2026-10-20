# Revit26_Plugin

_CLAUDE.md v1.1 — adds the ToolWindowShell UI standard._

Revit add-in ("SloperPro" ribbon tab, see `App.cs`), single monolithic
`Revit26_Plugin.csproj` (`net8.0-windows`) with ~60 independent tools under `Menu/`.

## Build prerequisites

- `RevitAPI.dll` / `RevitAPIUI.dll` are referenced via a local `HintPath`
  (currently `D:\Github\Revit_26\Apiref\...` in the `.csproj`) — not NuGet,
  not committed. A build will fail on any machine without a Revit install at
  that path; update the `HintPath` rather than trying to restore it as a package.

## Folder & versioning convention

Tools live at `Menu/<Category>/<Subcategory>/<ToolName>_V0xx/`, each with its
own subtree:

```
<ToolName>_V0xx/
  Commands/        IExternalCommand — thin, Revit-side entry point only
  Core/
    Models/        POCOs
    Services/      business/geometry logic — zero Revit API dependency
    (Engine/Parameters/... as needed)
  Infrastructure/
    ExternalEvents/  ExternalEventHandler + event-raising manager
    Helpers/
  UI/
    ViewModels/
    Views/         WPF window (.xaml/.xaml.cs)
```

A new tool version is a **new `_V0xx` folder**, not an in-place edit — older
versions are routinely left on disk even after a newer one becomes "live."
"Live" means wired into a `Menu/00_Push_Button_Menu_items/Ribbon/*Ribbon.cs`
file that's called from `App.cs:InitializeRibbonPanels`. A folder with no
`PushButtonData` referencing it is dead/orphaned — check the ribbon file
before assuming a version is in use, and don't delete old version folders
without confirming nothing references them.

Some tool folders only contain a `.zip` with no extracted source — these were
never wired up and can't have a PushButton added until unzipped.

## Established architecture pattern

(Confirmed by a full repo audit — see `Claude/REVIT_PLUGIN_AUDIT.md`.)

- `Command.cs` (`IExternalCommand.Execute`) wraps its whole body in try/catch:
  `OperationCanceledException` → `Result.Cancelled`, anything else →
  `TaskDialog.Show` + `Result.Failed`. Keep it thin; move logic into
  `Core/Services`.
- `Core/Services` classes have **no Revit API dependency** — pure logic,
  testable in principle even though no test project exists yet.
- All Revit document mutation happens through `ExternalEvent.Raise()` /
  an `IExternalEventHandler` in `Infrastructure/ExternalEvents/` — created
  once (field, constructor/window-load), never re-created per click.
  (A few older ViewModels commit transactions directly from a modeless
  window's UI-thread click handler instead — known, intentionally left
  alone; don't "fix" this pattern on sight, it's a confirmed exception.)
- Every `Transaction`/`TransactionGroup` is in a `using` block.
- WPF windows are modeless (`window.Show()`, not `ShowDialog()`); set the
  window's `Owner` via `WindowInteropHelper` — stated convention, but
  historically missed by some tools, so don't assume it's already done.
- Settings load via `JsonSerializer.Deserialize` wrapped in try/catch,
  falling back to defaults on failure.

## ToolWindowShell — tool window standard (v1.1)

Every tool window's content is a `Shared/Controls/ToolWindowShell`
(`xmlns:shell="clr-namespace:Revit26_Plugin.Shared.Controls"`) with three
zones (it's a templated `Control`, template in
`ToolWindowShellTemplate.xaml` — not a UserControl, so `x:Name` inside
tool content still works):

- **Header** — `Title`, `Subtitle`, `Version` (navy bar, built in) plus
  `HeaderContent`: status badges and metric tiles. Never scrolls.
- **Body** — `BodyContent`, hosted in the shell's single ScrollViewer.
  Inputs, expanders, DataGrids **and the log** live here.
- **Footer** — status strip bound to the ViewModel's `IsRunning`,
  `Progress` (0–100) and `SummaryText`, then `FooterContent`: secondary
  actions left, Primary → Close right. Never scrolls.

Rules:
- No `ScrollViewer` inside tool content — the shell body is the only one.
- Every DataGrid and log list sets
  `shell:ShellBehaviors.MaxHeightRatio` (fraction of window height,
  e.g. `0.45` grid / `0.25` log) and `shell:ShellBehaviors.ForwardMouseWheel="True"`.
- No inline colours (`#RRGGBB`, named colours) in window XAML — add a
  `Color` + `Brush…` token (or a style) to `Shared/SharedStyles.xaml`.
  `Transparent` is the one exception and stays inline (Rafi, 2026-10-04).
  Metric tiles use `MetricTile[Info|Danger|Success|Warning]` with the
  matching `MetricTileLabel…` / `MetricTileNumber…` text styles.
- The shell's `IsRunning` / `Progress` / `SummaryText` bind to the
  ViewModel. If it already has an equivalent (e.g. `IsBusy`,
  `RunSummary`), bind to that; only add what's missing, as a plain
  observable property (per Rafi's decision, 2026-10-04). A tool that never
  reports a percentage sets `IsIndeterminate="True"` on the shell so the
  bar animates while running. Don't otherwise
  change ViewModel logic, Revit API code or transactions during a shell
  migration.
- A migration is a normal version bump: new `_V0xx` folder + namespace.

Reference implementation: SectionAutoRenamer V026.

## Ribbon & icons — read before touching either

Full detail in `Claude/LOGO PROMPT FOLDER/Ribbon_Code_Notes.md`. Sharp edges:

- Only the parent `PulldownButton` gets an icon by default. To icon an
  individual `PushButtonData`, capture `AddPushButton`'s return value (cast
  to `PushButton`) and set `.Image`/`.LargeImage` on it explicitly.
- `Resources/Icons/**/*.png` is embedded by one wildcard in the `.csproj`;
  a new PNG dropped in a category folder is picked up automatically. A wrong
  (or wrong-case) path in `ImageUtils.Load` throws `FileNotFoundException`
  at `OnStartup` the first time that button is built.
- `QuickAcces.cs` (note: not `*Ribbon*.cs`) defines `QuickAccessRibbon`,
  called from `App.cs` — easy to miss in a filename-based search.

## Known gaps — don't "fix" these without asking

Deliberate/accepted per the audit, not oversights to patch reflexively:
- No test project (no xUnit/NUnit anywhere).
- No `AssemblyVersion`/`AssemblyInfo.cs` — single assembly, SDK default
  `1.0.0.0`; the `V0xx` in folder/class names tracks tool revision, not
  assembly version.
- No `SchemaVersion` on settings POCOs — no migration logic exists to need one.
- The `.addin` manifest is `Installer/rf12_Revit_2026.addin` (since 4.0.1); the installer
  scripts also write their own copy at install time.

## Decision trail in code

Non-obvious decisions are recorded as dated comments in the relevant file
header rather than in a separate doc, e.g. `// per Rafi's confirmed decision
(2026-07-21)`. Check a tool's `Commands/*.cs` header before assuming
behavior is accidental — it may be a deliberate, previously-confirmed choice.

## Other docs

- `Claude/REVIT_PLUGIN_AUDIT.md` — repeatable audit checklist (memory,
  threading, error handling, structure) + findings log.
- `Claude/LOGO PROMPT FOLDER/Ribbon_Code_Notes.md` — ribbon/icon wiring notes.
- `Claude/LOGO PROMPT FOLDER/Icon_Prompts.md` — icon generation prompts per
  live button.
