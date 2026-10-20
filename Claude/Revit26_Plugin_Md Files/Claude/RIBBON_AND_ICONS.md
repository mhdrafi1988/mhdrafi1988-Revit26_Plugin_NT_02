# Ribbon & Icons

Living reference for adding/changing ribbon buttons and their icons. For the
history of the original cleanup pass this consolidates, see
`Claude/LOGO PROMPT FOLDER/Ribbon_Code_Notes.md` (kept as a record, not
current status — e.g. it describes a flat, partially-unembedded icon set;
icons are now organized into per-category subfolders and, as of this
writing, everything a `*Ribbon.cs` file actually references is embedded).

## Where things live

- `Menu/00_Push_Button_Menu_items/Ribbon/*Ribbon.cs` — one file per category
  (`RoofToolsRibbon`, `FloorToolsRibbon`, ... ), each with a static
  `Build(app, tabName, assemblyPath)` called from `App.cs`. Also
  `QuickAcces.cs` → `QuickAccessRibbon` — doesn't match `*Ribbon.cs` by name,
  easy to miss when grepping for ribbon files.
- `Menu/00_Push_Button_Menu_items/Ribbon/RibbonLayoutHelper.cs` — shared
  layout/tooltip helpers; use these rather than calling `panel.AddItem` /
  `AddStackedItems` directly (see below for why).
- `Resources/Icons/<Category>/<Name>_<16|32>.png` — one subfolder per
  category (`RoofTools`, `Dimensions`, `FloorTools`, `Manage`, `SetupTools`,
  `SheetTools`, `ViewTools`, `DetailLiner`).
- `Utilities/ImageUtils.cs` — `ImageUtils.Load(resourcePath)` loads an
  embedded PNG by its dotted manifest-resource name and throws
  `FileNotFoundException` if it isn't embedded.

## Adding a new icon

Drop the PNG in the right `Resources/Icons/<Category>/` folder. That's it:
`Revit26_Plugin.csproj` embeds every `Resources\Icons\**\*.png` through one
wildcard (with a matching `<None Remove>` so the SDK doesn't also include it
as `None`):

```xml
<None Remove="Resources\Icons\**\*.png" />
<EmbeddedResource Include="Resources\Icons\**\*.png" />
```

A typo in the resource path doesn't fail the build; it fails at runtime the
first time `ImageUtils.Load` is asked for that path (i.e. the first time the
button is built in `OnStartup`). Names are case-sensitive.

The manifest-resource path is the `<RootNamespace>` (`Revit26_Plugin_Revised`) followed by the folder as dotted segments:
`Resources/Icons/RoofTools/by_point_16.png` on disk →
`"Revit26_Plugin_Revised.Resources.Icons.RoofTools.by_point_16.png"` in code.

## 16px vs 32px

Convention is one icon at each size per tool: `<Name>_16.png` for
`PushButtonData.Image` (used for individual buttons inside a dropdown/stack)
and `<Name>_32.png` for `.LargeImage` (used for a pulldown's own header icon,
or a lone full-size button). Not every tool has both generated — check
before assuming a `_32` exists just because a `_16` does, or vice versa.

## Adding a push button

`PulldownButton.AddPushButton(...)` and `RibbonPanel.AddItem(...)` both
return the created item — **capture it and cast to set an icon**:

```csharp
var btn = (PushButton)pulldown.AddPushButton(new PushButtonData(
    "Btn_MyTool_V001", "My Tool V001", assemblyPath,
    "Revit26_Plugin_Revised.MyTool.V001.Commands.MyToolCommand")
{
    Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.RoofTools.MyNewTool_16.png"),
    ToolTip = RibbonLayoutHelper.VersionTip("My Tool", "V001", "one-line description")
});
```

Always set `Image` on the `PushButtonData` object itself (as above) rather
than on the object returned by `AddPushButton` — that's the pattern every
`*Ribbon.cs` file uses. Always build the tooltip through
`RibbonLayoutHelper.VersionTip(tool, version, detail?)` so the version
string is never accidentally left off.

## Stacking / pulldowns — use `RibbonLayoutHelper`, not the Revit API directly

`RibbonLayoutHelper` exists because of two real Revit ribbon quirks it works
around; skipping it reintroduces both:

- **Stacked-item label bug**: inside `panel.AddStackedItems(...)`, a
  button's `Text` is frequently dropped at render time (icon shows, no
  label) — never happens for a lone `AddItem` button. `AddStackedButtons`
  re-applies `ItemText`/`ToolTip` on the returned `RibbonItem` after the
  fact to force it to bind. If you ever add buttons via `AddStackedItems`
  directly, you'll hit this.
- **Lone leftover button with no `LargeImage`**: a stack of 1 (odd item
  left over after grouping in 3s) renders as a full-size `AddItem` button,
  which needs `LargeImage` set or it shows a blank icon slot.
  `AddStackedButtons` backfills `LargeImage = Image` for this case
  automatically — only when you skip the helper does this need doing by hand.

For 2+ versions of the same tool under one dropdown, the wiring is
**two calls, in order**:

```csharp
var pulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_MyTool", "My Tool", primaryVersion);
// ... include pulldownData in the list passed to AddStackedButtons ...
var created = RibbonLayoutHelper.AddStackedButtons(panel, items);
RibbonLayoutHelper.WirePulldownButton(created, "Pulldown_MyTool", primaryVersion, olderVersion1, olderVersion2);
```

`CreatePulldownButtonData` only sets a placeholder icon/tooltip from the
primary (newest) version; `WirePulldownButton` must run afterward with the
full version list (primary first) to actually populate the dropdown and
build the combined "N versions" tooltip via `VersionTip`'s sibling,
`BuildVersionsTip`. Only the primary `PushButtonData` should carry an icon —
the other versions carry their own icon/text once added to the dropdown.

**Keep only the latest two versions of each tool in its dropdown** (plugin 3.0.0). When a new
version is added, drop the oldest button from the dropdown *and* delete its source folder with
`git rm` (history keeps it) — unless another tool still compiles against it. Check with a repo-wide
search for the old namespace (`Revit26_Plugin.<Tool>.V0nn`) and a build before deleting.

## Panel layout — keep the tab narrow

Revit shrinks a ribbon tab that is wider than its window: first it drops the names of stacked
buttons, starting with the right-most panels, then it folds whole panels into one dropdown.
Every button should show its icon **and** name, so (since plugin 2.4.0):

- **No one-button panels.** Add a new tool to the panel it belongs with instead of creating a
  panel for it. The one-button panel for a moved tool keeps its pulldown builder in its old file
  (`FloorToolsRibbon.CreateFromRoomsPulldown`, `DimensionsRibbon.CreateDetailLineDimPulldown`)
  and the receiving panel stacks it.
- **No lone large buttons.** `AddStackedButtons` stacks 3 per column and splits 4 as 2 + 2, so
  only a one-item panel gets a full-size button.
- **Short labels.** A stacked column is as wide as its longest label, so keep labels to about
  15 characters; the tooltip carries the full tool name.
- **Panel names are part of shortcut ids.** Revit keys keyboard shortcuts and QAT items on
  tab + panel + button name, so moving a button to another panel (or renaming a panel) resets
  them. Never rename a panel just for its title; note moved buttons in the CHANGELOG user note.

Current tab, left to right (13 panels): Utilities · Roof Slope · Shape Points · Roof Tools ·
Compare · View Create · View Place · View Rename · Family Tools · Project Tools · Sheet Create ·
Detail Lines · Manage.

## Known trap: `QuickAcces.cs`

Filename doesn't contain "Ribbon", so a filename-based search for ribbon
files misses it. It's still called from `App.cs` and builds a real panel —
check it explicitly when auditing what's wired to the ribbon.

## Other files in this area

- `Claude/LOGO PROMPT FOLDER/Icon_Prompts.md` — AI image-generation prompts,
  one per live button at the time it was written; regenerate/extend as
  buttons change rather than treating it as exhaustive or current.
- `Claude/LOGO PROMPT FOLDER/Ribbon_Code_Notes.md` — historical notes from
  the original PushButton/icon cleanup pass; superseded by this file for
  current conventions.
