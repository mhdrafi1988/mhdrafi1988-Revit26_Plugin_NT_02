# Revit26_Plugin — reference notes

_v2.2, 2026-11-03 (plugin 6.37.0): floor tools added. v2.1, 2026-11-03: preview Artifact is opened every tool update (Stop hook reminder). v2.0, 2026-11-02 (plugin 6.31.0): brought in line with the root `CLAUDE.md`. v1.1 (2026-10-04) added the
ToolWindowShell standard._

**The development standard is the root `CLAUDE.md`.** It covers build, folder layout, versioning, robustness,
documentation, UI previews, backward compatibility and the pre-release checklist. Where this file and the root one
disagree, the root one wins. The `Claude/` folder next to this file holds the deeper reference notes, by topic:

| File | What it covers |
|---|---|
| `Claude/ARCHITECTURE.md` | Runtime flow: startup → ribbon → command → window → external event |
| `Claude/RIBBON_AND_ICONS.md` | Ribbon layout of both tabs, `RibbonLayoutHelper`, icons |
| `Claude/TOOL_VERSIONING.md` | What `_V<NNN>` and `ToolCatalog` versions mean, and a tool version's states |
| `Claude/UI_UX_PATTERNS.md` | Window patterns: shared styles, filtering, grouping, logging, tabs |
| `Claude/CHANGELOG.md` | Engineering notes (the root `CHANGELOG.md` is the release log) |

## Project at a glance

- Revit 2026 add-in, .NET 8 / WPF, one project: `Revit26_Plugin_Revised.csproj` (root namespace and assembly
  `Revit26_Plugin_Revised`). The Revit API comes from the `Autodesk.Revit.SDK` NuGet package, so it builds on any
  machine with `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true`.
- Two ribbon tabs, **SloperPro_Rev_88-MDL** (Modelling) and **SloperPro_Rev_88-DCM** (Documents), built in
  `App.cs : InitializeRibbonPanels`. See `Claude/RIBBON_AND_ICONS.md` for every panel.
- Tools live in `Menu/<Area>/<Group>/<Tool>_V<NNN>/` with `Commands`, `Core`, `UI` and `Infrastructure` folders.
  Each tool's semantic version is in `Shared/Services/ToolCatalog.cs`.
- Unit tests are in `Tests/<Project>` (16 projects), and run with `dotnet test Tests/<Project>`. They link the
  tool's Revit-free files rather than copying them.
- UI previews for tool changes are in `Claude/Previews/` and are published as claude.ai Artifacts (root
  `CLAUDE.md`, "UI previews"). Every tool update opens the Artifact for the owner in the same turn, before
  the change is reported done; a Stop hook (`.claude/hooks/check-preview.sh`) reminds Claude if it is missing.

## Established patterns

- **Entry points:** every `IExternalCommand.Execute` and `IExternalEventHandler.Execute` delegates to
  `ToolGuard.RunCommand` / `ToolGuard.RunHandler`. ToolGuard turns `OperationCanceledException` into
  `Result.Cancelled`, and reports anything else with the tool's name and version from `ToolCatalog`, logged to
  `%AppData%\Revit26_Plugin_Revised\Logs`.
- **Model changes** happen through an `IExternalEventHandler` raised from the window (one `ExternalEvent` per tool,
  created once), inside `using` `Transaction` / `TransactionGroup` blocks. A few older ViewModels commit from a
  modeless window's click handler instead. This is a known exception; don't "fix" it on sight.
- **Windows are modeless** (`Show()`) and owned by Revit's main window. Newer tools (Auto Slope By Point V032+,
  By Drain V013, the Floor Slope tools) also allow only one open window at a time: a second click brings the open
  one to the front (`TryActivateOpenWindow`).
- **Settings** are JSON under `%AppData%\Revit26_Plugin_Revised\<Tool>\`, read with try/catch and defaults on
  failure. New settings formats carry a version field (root `CLAUDE.md`, Backward compatibility 4).
- **Floor versions of roof tools** (plugins 6.31.0–6.37.0) are new tools at `_V001`, copied from the latest roof
  version only, with Roof → Floor names. They read a floor's sketch through `Shared/Services/FloorSketchProfile`
  (floors have no `GetProfiles()`), refuse floors they can't change through `FloorShapeEditChecks`, and ask before
  changing a floor type with no Variable layer. The full roof → floor list is in `Claude/TOOL_VERSIONING.md`.
- **`Core/`** holds the tool's logic. Only some tools keep it free of the Revit API: 85 of the 105 `Core/` folders
  outside WIP use Revit types. Where a tool has unit tests, the tested files are the Revit-free ones.

## ToolWindowShell — tool window standard

Every tool window's content is a `Shared/Controls/ToolWindowShell`
(`xmlns:shell="clr-namespace:Revit26_Plugin_Revised.Shared.Controls"`). It is a templated `Control` (template in
`ToolWindowShellTemplate.xaml`), not a UserControl, so `x:Name` inside tool content still works. It has three zones:

- **Header:** `Title`, `Subtitle`, `Version` (navy bar, built in), plus `HeaderContent` for status badges and metric
  tiles. It never scrolls.
- **Body:** `BodyContent`, hosted in the shell's single ScrollViewer. Inputs, expanders, DataGrids and the log live
  here.
- **Footer:** a status strip bound to the ViewModel's `IsRunning`, `Progress` (0–100) and `SummaryText`, then
  `FooterContent`: secondary actions on the left, Primary → Close on the right. It never scrolls.

Rules:
- No `ScrollViewer` inside tool content; the shell body is the only one.
- Every DataGrid and log list sets `shell:ShellBehaviors.MaxHeightRatio` (a fraction of the window height, e.g.
  `0.45` for a grid and `0.25` for a log) and `shell:ShellBehaviors.ForwardMouseWheel="True"`.
- No inline colours (`#RRGGBB` or named colours) in window XAML. Add a `Color` + `Brush…` token, or a style, to
  `Shared/SharedStyles.xaml` (semantic tokens are v4.0). `Transparent` is the one exception and stays inline (Rafi,
  2026-10-04). Metric tiles use `MetricTile[Info|Danger|Success|Warning]` with the matching `MetricTileLabel…` /
  `MetricTileNumber…` text styles.
- The shell's `IsRunning` / `Progress` / `SummaryText` bind to the ViewModel. If it already has an equivalent (e.g.
  `IsBusy`, `RunSummary`), bind to that, and add only what's missing, as a plain observable property (Rafi,
  2026-10-04). A tool that never reports a percentage sets `IsIndeterminate="True"` on the shell.
- The window title and version badge come from `ToolCatalog` (`Name — vX.Y.Z`), never a hard-coded string.
- A shell migration is a normal tool change: change the tool in place and bump its `ToolCatalog` version (root
  `CLAUDE.md`). Don't otherwise change ViewModel logic, Revit API code or transactions during a migration.

Reference implementations: Section Auto Renamer V026, and Floor Slope By Drain V001 / By Point V001.

## Ribbon & icons — sharp edges

Full detail is in `Claude/RIBBON_AND_ICONS.md`.

- A command class is wired by its fully-qualified name **as a string**. A typo builds fine and only fails when
  the button is clicked.
- Build stacks, pulldowns and slide-outs through `RibbonLayoutHelper`, not the Revit API directly. It works around
  Revit's stacked-label bug and the blank-icon bug of a lone large button.
- `Resources/Icons/**/*.png` is embedded by one wildcard in the `.csproj`. A wrong (or wrong-case) path in
  `ImageUtils.Load` throws `FileNotFoundException` at `OnStartup` the first time that button is built.
- Never rename a ribbon button's internal name or a panel just for its title. Revit keys keyboard shortcuts and
  Quick Access Toolbar items on tab + panel + button name.

## Decision trail in code

Non-obvious decisions are recorded as dated comments in the relevant file header rather than in a separate doc,
e.g. `// per Rafi's confirmed decision (2026-07-21)`. Check a tool's `Commands/*.cs` header before assuming
behaviour is accidental. It may be a deliberate, previously confirmed choice.

## Other docs

Paths from the repository root:

- `Claude/REVIT_PLUGIN_AUDIT.md`: repeatable audit checklist (memory, threading, error handling, structure) and the
  2026-09-03 findings log.
- `Claude/LOGO PROMPT FOLDER/Icon_Prompts.md`: icon-generation prompts.
- `Claude/LOGO PROMPT FOLDER/Ribbon_Code_Notes.md`: historical notes from the first ribbon/icon cleanup.
- `Claude/Previews/`: HTML UI previews of tool windows.
