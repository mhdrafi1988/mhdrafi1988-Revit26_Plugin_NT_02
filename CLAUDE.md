# SloperPro (Revit26_Plugin) — Development Standard

Revit 2026 add-in, .NET 8 / WPF. These rules apply to every change, by people and by Claude.

## Build

```bash
dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true
```

Builds on Linux too (Revit API comes from the `Autodesk.Revit.SDK` NuGet package). A change is not
done until it builds with 0 errors. Running it still needs Revit on Windows.

## Repository conventions

- **Git is the version history.** Never copy a tool folder to make a new version (`Tool_V005` next to
  `Tool_V004`), and never commit `.zip` backups. Change the tool in place and bump its version.
  The one exception is shareable tool packages: a `.zip` of a tool's source, made with
  `git archive` from `main`, may go in `Installer/Output/ToolPackages/` (one per tool, replaced in place).
- **Folder layout:** `Menu/<Area>/<Group>/<Tool>_V<NNN>/` holding `Commands`, `Core`, `UI`,
  `Infrastructure` directly. No wrapper levels (`Tool_V003/Tool_V003/…`), no spaces, `&`, `(1)`,
  `_UPDATED` or `_0`/`_01` suffixes in folder names. Unfinished tools go in `Menu/RoofTools/WIP`,
  which the `.csproj` excludes; the only other exclusion is `Tests/` (unit-test projects, run with
  `dotnet test Tests/<Project>`).
- **View model naming:** a tool's view model class and file are named `ViewModel_<ToolName>`
  (e.g. `ViewModel_DetailLineManager` in `ViewModel_DetailLineManager.cs`), never a bare
  `MainViewModel`, so it is identifiable in searches, stack traces and `using` lists. Partial
  files add a suffix: `ViewModel_DetailLineManager.Actions.cs`. `<ToolName>` is the tool name
  without the version. Applies to new tools; do not rename existing view models unless the owner
  asks (a rename must also update the XAML `DataContext` and any `x:Class`/`clr-namespace` use).
- **Tool versions** live in `Shared/Services/ToolCatalog.cs` (one entry per tool, semantic version).
  The ribbon tooltip, window title and error dialogs read from it. A new tool gets a new entry there.
- **Plugin version** is `<Version>` in `Revit26_Plugin_Revised.csproj`.
- **Namespace:** the root namespace is `Revit26_Plugin_Revised` (`<RootNamespace>` in the `.csproj`;
  renamed from `Revit26_Plugin` in 4.0.0). Tool namespaces are `Revit26_Plugin_Revised.<Tool>.V<NNN>`.
  The assembly is `Revit26_Plugin_Revised.dll` (`<AssemblyName>`, renamed in 5.0.0), so pack URIs
  read `/Revit26_Plugin_Revised;component/…`. The project file is `Revit26_Plugin_Revised.csproj`.
  Settings and logs live under `%AppData%\Revit26_Plugin_Revised` (`<Tool>\settings*.json`, `Logs\`);
  keep using that folder for new tools.
- **Add-in manifest** is `Installer/rf12_Revit_2026.addin` (same file name, `AddInId` and
  `FullClassName` as the one the `Installer/Output/*.iss` scripts write). Keep them in sync.
- **Ribbon** files are in `Menu/00_Push_Button_Menu_items/Ribbon`. One button runs one tool; use a
  dropdown only to group different tools. Labels show the tool name, never a version number.
  Tooltips use `ToolCatalog.<Tool>.Tip(...)`. When a panel or button is added, moved or removed, update the
  layout tables in `Claude/Revit26_Plugin_Md Files/Claude/RIBBON_AND_ICONS.md` and the Ribbon section of
  `README.md` in the same change.
- **Dropdown version order:** when a pulldown lists coexisting versions of the same tool (via
  `RibbonLayoutHelper.CreatePulldownButtonData` / `WirePulldownButton`), order them highest version
  number first, every older version below it in descending order — never "current version first".
  The highest version is also the one that supplies the pulldown button's own icon (the `primary`
  argument to `CreatePulldownButtonData`).
- **Slide-out:** a panel gets a slide-out (`panel.AddSlideOut()`, the arrow on the panel title bar)
  wherever one is needed and possible, for example for rarely used, advanced, settings or help
  tools that would crowd the visible panel. Never add one on your own: discuss it with the owner
  first (which panel, which tools go inside), then add it. A panel has at most one slide-out, and
  everything added after `AddSlideOut()` goes inside it.
- **Entry points:** every `IExternalCommand.Execute` and `IExternalEventHandler.Execute` delegates to
  `ToolGuard.RunCommand` / `ToolGuard.RunHandler` (see any existing command). New ones must too.

## Robustness

1. No exception may reach Revit unhandled. `ToolGuard` is the last line of defence; still catch and
   handle expected failures where they happen, with a clear message.
2. Make model changes inside `using` `Transaction` / `TransactionGroup` blocks so failures roll back.
   Never commit partial work after an error.
3. Validate inputs before acting: null elements, missing parameters, wrong view type, read-only or
   workshared elements.
4. Handle edge cases: linked models, groups, in-place families, elements owned by other users (ACC /
   worksharing — check `WorksharingUtils.GetCheckoutStatus`).
5. Report errors in the tool's UI or log; never fail silently. Unexpected errors go to
   `%AppData%\Revit26_Plugin_Revised\Logs` through `ToolGuard.Report`.

## Documentation

1. Add `///` XML comments to all public classes and methods you add or change.
2. Update `CHANGELOG.md` for every user-visible change (version, date, Added / Changed / Fixed /
   Removed, and a user note for any behaviour change).
3. Use semantic versioning (MAJOR.MINOR.PATCH): MAJOR for breaking changes or removals, MINOR for
   new features, PATCH for fixes. Bump the tool in `ToolCatalog.cs` and the plugin in the `.csproj`.
4. Every tool update needs an HTML UI preview, published as a claude.ai Artifact (next section).

## UI previews (HTML artifact)

Required for **every tool update**: a new tool, a new `_V<NNN>` folder, or any change that bumps the
tool's version in `ToolCatalog.cs`. The preview is part of the change. The change is not done until
the preview is updated and published.

1. **File:** `Claude/Previews/<ToolFolder>_Window.html`, for example `RoofPointsAndSlope_V001_Window.html`.
   Keep one file per tool folder and update it in place; git keeps the history. Tools released
   together may share one page named after the group, such as `FloorSlope_V001_Windows.html`. Its header
   comment then lists every tool it covers.
2. **Content:** a static mock-up of what the user sees, in the state that shows the change. That means
   the tool window(s), plus any new or changed ribbon button, tooltip or dialog.
   - Take the labels, button text and window title from the XAML and view model, and the version
     from `ToolCatalog`.
   - Mark what is new or changed with a green tag.
   - Use realistic sample data, and say on the page that it is sample data. Never show real project
     or client names.
   - A change with no visible effect still updates the version in the title bar, and adds one line
     saying what changed.
3. **Format:** the claude.ai Artifact page format.
   - Start the file with `<title>`. Leave out `<!doctype>`, `<html>`, `<head>` and `<body>`.
   - Define the colours as tokens on `:root`, taken from `Shared/SharedStyles.xaml`, and set them
     explicitly, the `body` background included.
   - Make it work at phone width: give `body` a 16px side gutter, and put each fixed-width window in
     its own `overflow-x: auto` box.
   - Load no external files.
4. **Header comment** (an HTML comment after `<title>`): the tool(s) and versions, the plugin version,
   the XAML file(s) the page mirrors, and the Artifact link.
5. **Publish:** publish the page as a claude.ai Artifact. If the header comment already holds a link,
   republish to that link so it stays the same, and keep the link in the comment. Open the Artifact
   for the owner in the same turn as the tool change, before reporting the change as done, and put
   the link in the PR description. A Stop hook (`.claude/hooks/check-preview.sh`) reminds Claude when
   `ToolCatalog.cs` changed on the branch but no `Claude/Previews/*.html` did.
6. Older previews that do not follow this format yet are brought up to it the next time their tool
   changes.

## Backward compatibility

1. Never change a ribbon button's internal name (first `PushButtonData` argument), an
   `IExternalCommand` class name, or an add-in GUID. Change a visible label only when asked.
2. Change a namespace or the assembly name only when the owner asks for it (the 4.0.0 namespace
   and 5.0.0 assembly renames to `Revit26_Plugin_Revised` were such requests). Then do it in one
   change. A namespace change covers C# and XAML (`x:Class`, `clr-namespace:`), the command
   class-name strings in the ribbon, `<RootNamespace>` and the embedded-resource names that follow
   it (`ImageUtils.Load` paths, `Properties.Resources`), and `FullClassName` in
   `Installer/rf12_Revit_2026.addin` and the installer scripts. An assembly-name change covers
   `<AssemblyName>`, every `/<Assembly>;component/` pack URI and `assembly=` xmlns, `<Assembly>` in
   the `.addin`, and the DLL / `.deps.json` names in the installer scripts. Either breaks existing
   installs, so bump MAJOR and add a CHANGELOG user note.
3. Never rename or remove shared parameters, parameter GUIDs or schema fields. Create new ones only if
   missing, and keep working without them.
4. Add a version field to JSON / Excel / config formats; read old and new, or migrate automatically.
5. Never change an existing Extensible Storage schema GUID or its fields; create a new schema version
   and migrate.
6. New options default to the old behaviour.
7. Keep export column names and order unchanged; append new columns at the end.
8. Keep Revit-version-specific API calls behind one helper so 2026 vs 2027 differences stay isolated.
9. Deprecate before deleting: keep the old behaviour for one or two releases with a warning, then
   remove.

## Pre-release checklist

1. Test on a model processed with the previous plugin version.
2. Run the old workflow end to end and compare outputs.
3. Confirm old settings and config files still load.
4. Update `CHANGELOG.md` and the version numbers.
5. Update the tool's HTML preview in `Claude/Previews/` and publish it as an Artifact (see UI previews).
