# Architecture

How a request flows through this add-in, end to end. Updated 2026-11-02 (plugin 6.31.0). The rules (folder
layout, versioning, robustness) are in the root `CLAUDE.md`. This file is the runtime picture: what calls what,
and where the sharp edges are.

## 1. Startup → ribbon

`App.cs : OnStartup` creates two ribbon tabs, **SloperPro_Rev_88-MDL** (Modelling) and **SloperPro_Rev_88-DCM**
(Documents), then `InitializeRibbonPanels` calls one builder per area from
`Menu/00_Push_Button_Menu_items/Ribbon/`. Panels appear in the order they are created. The full layout is in
`Claude/RIBBON_AND_ICONS.md`.

Each builder makes `RibbonPanel`s and `PushButtonData`, usually through `RibbonLayoutHelper`
(`AddStackedButtons`, `AddStackedButtonsWithSlideOut`, `CreatePulldownButtonData`, `WirePulldownButton`). Each
button is wired to a **command class by its fully-qualified name, as a string**:

```csharp
new PushButtonData("Btn_FloorSlopeByDrain_V001", "By Drain", assemblyPath,
    "Revit26_Plugin_Revised.FloorSlopeByDrain.V001.Commands.FloorSlopeByDrainCommand")
```

This string is not compiler-checked. A typo, or a namespace that drifts from the ribbon file, builds fine and only
breaks when the button is clicked in Revit. When renaming a namespace or moving a tool, grep the ribbon files for
the old fully-qualified name. Moving folders doesn't update it.

**The namespace does not always mirror the folder path.** `AutoSlopeByDrain_V013` lives under
`Menu/RoofTools/RoofSlope/ByDrains/`, but its code is in `Revit26_Plugin_Revised.MultiRoofSlopeByDrain.V013`, a
name kept from when it was split out as the multi-roof variant. To find what a button runs, read the type string in
the ribbon file rather than inferring it from the folder name.

## 2. Per-tool request lifecycle

```
PushButton click
  → Command.Execute(ExternalCommandData)                      [Commands/]
      delegates to ToolGuard.RunCommand(GetType(), …, ExecuteCore)
      ExecuteCore: bring an open window to the front if there is one (newer tools),
      pick elements (PickObject(s)), validate them, read what the window needs.
      Synchronous API work is safe here because no window is open yet.
  → new Window(…).Show()                                      [UI/Views, UI/ViewModels]
      modeless; the user edits settings, previews, adjusts options
  → ViewModel builds a request/payload (Core/Models), hands it to the handler,
    and raises the tool's ExternalEvent
  → Handler.Execute(UIApplication)                            [Infrastructure/ExternalEvents/]
      delegates to ToolGuard.RunHandler; runs on Revit's API thread, opens a
      TransactionGroup, does the model change (often through a Core/Engine or
      Core/Services class), assimilates or rolls back, then reports back to the
      ViewModel (callback + Dispatcher for the cross-thread UI update)
```

- **ToolGuard** (`Shared/Services/ToolGuard.cs`) is the last line of defence. Cancellation (Esc during a pick)
  returns `Result.Cancelled`. Anything else is shown with the tool's name and version from `ToolCatalog`, and
  written to `%AppData%\Revit26_Plugin_Revised\Logs`.
- **EventManager:** each tool's `Infrastructure/ExternalEvents/*EventManager.cs` (or a static holder in the handler
  file) keeps one handler and one `ExternalEvent`, created once. `Init()` no-ops when already initialised. Most tools
  call it from the ViewModel. A few call it from the Command, and none from `App.cs`, whatever an older header
  comment says. Follow the repo pattern.

## 3. Layer dependency direction

```
Commands/         → Core/, Infrastructure/, UI/
UI/ViewModels/    → Core/, Infrastructure/ExternalEvents/ (to raise events)
Infrastructure/   → Core/, Revit API
Core/             → plain .NET where possible; many tools also use Revit types here
```

`Core/` is meant for the tool's logic. It is **not** reliably free of the Revit API: 85 of the 105 `Core/` folders
outside WIP use Revit types (`XYZ`, `SlabShapeVertex`, `Element`…). The newer split, seen in Auto Slope By Point
V033 and Floor Slope By Point V001, keeps pure maths in `Core/Services` (`ShortestPathSolver`, `RidgeFinder`,
`SpatialGrid2D`) and Revit calls in `Infrastructure/` (`RoofOps` / `FloorOps`). The unit tests in `Tests/` link
exactly those Revit-free files. Copy that split when a tool's logic needs tests.

## 4. Shared cross-tool code

`Shared/` holds code used by many tools. A change here has a repo-wide blast radius, so check other tools' usages
before changing a signature.

| Path | Namespace | What |
|---|---|---|
| `Services/ToolGuard.cs` | `Shared.Services` | Entry-point guard, error reporting and logs |
| `Services/ToolCatalog.cs` | `Shared.Services` | Tool names and semantic versions (window titles, tooltips, errors) |
| `Services/SettingsService.cs`, `JsonFileStore.cs` | `Shared.Services` | JSON files under `%AppData%\Revit26_Plugin_Revised\<Tool>`. `SettingsService` falls back to defaults; `JsonFileStore` throws on a failed save so the tool can tell the user |
| `Services/LogExportService.cs`, `LogClipboardService.cs` | `Shared.Services` | Save and copy the log |
| `Services/ModelIdentity.cs` | `Shared.Services` | Stable identity of a project model, for filing per-model data (saved sets, settings) |
| `Services/FloorShapeEditChecks.cs` | `Shared.Services` | Floor pre-checks and the no-Variable-layer dialog (Floor Slope tools) |
| `Controls/ToolWindowShell.cs`, `ToolWindowShellTemplate.xaml`, `ShellBehaviors.cs` | `Shared.Controls` | The standard tool window (see `CLAUDE.md` next to this folder) |
| `ViewModels/ToolViewModelBase.cs` | `Shared.ViewModels` | Base ViewModel with the shared log and shell properties |
| `LogEntry.cs`, `LogLevel.cs`, `Converters.cs` | `Shared.Models` | The one log model, and the shared value converters |
| `SharedStyles.xaml` | resource dictionary | Colour tokens and styles (v4.0 semantic tokens) |

All namespaces start with `Revit26_Plugin_Revised.`

## 5. Multi-item batch pattern

Tools that change several picked elements in one run (Auto Slope By Drain V013, Floor Slope By Drain V001) use
nested groups, in `Infrastructure/ExternalEvents/DrainJobs.cs`:

- **One outer `TransactionGroup` per run**, so the whole run is one Undo step.
- **Inside it, one `TransactionGroup` per element.** It is `Assimilate()`d when that element succeeds, and
  `RollBack()`ed when it fails or is cancelled, so a failed element is left exactly as it was.
- **The outer group is assimilated when at least one element succeeded**, and rolled back otherwise.
- A cancel stops later elements from starting. Elements that already finished stay applied.

If you add batch support to another tool, copy this pattern.
