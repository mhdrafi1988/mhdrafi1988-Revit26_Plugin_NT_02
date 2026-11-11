# Ribbon & Icons

Living reference for adding or changing ribbon buttons and their icons. Updated 2026-11-03 (plugin 6.37.0) from
`App.cs` and `Menu/00_Push_Button_Menu_items/Ribbon/*.cs`. The rules themselves are in the root `CLAUDE.md`
(Repository conventions: Ribbon, Dropdown version order, Slide-out); this file is the how-to and the current
layout. For the original cleanup pass, see `Claude/LOGO PROMPT FOLDER/Ribbon_Code_Notes.md`, which is kept as a
record and not current.

## Where things live

- `App.cs : InitializeRibbonPanels` creates four tabs and calls the builders in order. Panels appear in the order
  they are created.
  - **Rafi_Pro_MOds** (model 1): `RoofToolsRibbon.Build` (which also calls `FloorToolsRibbon.BuildSlopePanel`,
    `BuildPointsPanel`, `BuildLinesPanel` and `BuildEditPanel`).
  - **Rafi_Pro_MOds2** (model 2): `RoofToolsRibbon.BuildEditComparePanels`, `DetailLInesRibbon.Build`,
    `RoofToolsRibbon.BuildUtilities`.
  - **Rafi_Pro_Docs** (sheets 1): `ViewToolsRibbon` (which also calls `FloorToolsRibbon.BuildViewsPanel`),
    `SheetToolsRibbon`.
  - **Rafi_Pro_Docs2** (sheets 2): `TaggingRibbon`, `SetupRibbon`, `ManageRibbon`.
- `Menu/00_Push_Button_Menu_items/Ribbon/`: `RoofToolsRibbon.cs`, `FloorRibbon.cs` (class `FloorToolsRibbon`),
  `DetailLInesRibbon.cs`, `DimensionsRibbon.cs`, `ViewToolsRibbon.cs`, `TaggingRibbon.cs`, `SheetToolsRibbon.cs`,
  `SetupRibbon.cs`, `ManageRibbon.cs`, and `RibbonLayoutHelper.cs`. There is no `QuickAcces.cs` any more.
- Some builders only return a pulldown for another file's panel: `FloorToolsRibbon.CreateFromRoomsPulldown` (Roof
  Tools panel) and `DimensionsRibbon.CreateDetailLineDimPulldown` (Detail Lines panel).
- `Resources/Icons/<Category>/<Name>_<16|32>.png`, with one subfolder per category: `RoofTools`, `FloorTools`,
  `DetailLiner`, `Dimensions`, `ViewTools`, `Tagging`, `SheetTools`, `SetupTools`, `Manage`.
- `Utilities/ImageUtils.cs`: `ImageUtils.Load(resourcePath)` loads an embedded PNG by its dotted
  manifest-resource name, and throws `FileNotFoundException` if it isn't embedded.

## Current layout

Left to right. **▸** marks a slide-out (the arrow on the panel title bar); the items after it sit inside it.
Each item is a pulldown unless marked *button*. A pulldown lists its versions highest first.

### Modelling (`Rafi_Pro_MOds`, `Rafi_Pro_MOds2`)

| Tab | Panel | Items | Builder |
|---|---|---|---|
| Rafi_Pro_MOds | Roof Slope | By Point (V034, V033, V032, V030, Ridge V003, Multi Copies 30) · By Drain (V013, V012, V011, V007) · Points & Slope (V001) | `RoofToolsRibbon.Build` |
| Rafi_Pro_MOds | Floor Slope | By Drain *button* (Floor Slope By Drain V001) · By Point (By Point V001, Ridge V001, Multi Copies V001) · Points & Slope *button* (Floor Points & Slope V001) | `FloorToolsRibbon.BuildSlopePanel` |
| Rafi_Pro_MOds | Shape Points | Inner Loops · Outer Curve · Line Intersect · Loops + Perp. ▸ Vertex Reducer · Multi Points · Shape From Link | `RoofToolsRibbon.Build` |
| Rafi_Pro_MOds | Floor Points | Inner Loops *button* · Outer Curve *button* · Line Intersect *button* · Loops + Perp. *button* · Vertex Reducer *button* · Multi Points *button* · Shape From Link (large *button*) — each V001 | `FloorToolsRibbon.BuildPointsPanel` |
| Rafi_Pro_MOds | Floor Lines | Crease By Openings *button* · Crease By Points *button* · Floor Creaser *button* — each V001 | `FloorToolsRibbon.BuildLinesPanel` |
| Rafi_Pro_MOds | Floor Edit | Floor Types *button* · Name Builder *button* · Floor Tag *button* · Comparison *button* · Elevation Sync *button* · Floor EPS *button* — each V001 | `FloorToolsRibbon.BuildEditPanel` |
| Rafi_Pro_MOds | Roof Tools | Creaser Adv · Roof Tag · Floor From Rooms · Shape From Link | `RoofToolsRibbon.Build` |
| Rafi_Pro_MOds2 | Roof Edit | Name Builder · Island Splitter | `BuildEditComparePanels` |
| Rafi_Pro_MOds2 | Roof Plan View | Island Splitter (large *button*) | `BuildEditComparePanels` |
| Rafi_Pro_MOds2 | Roof Compare | Comparison · Elevation Sync | `BuildEditComparePanels` |
| Rafi_Pro_MOds2 | Roof Insulation | EPS 2D Pattern (V006, V005, V004, V002, V001) | `BuildEditComparePanels` |
| Rafi_Pro_MOds2 | Detail Lines | From Links · Closed Loop · Line Dims · Line Manager (large) | `DetailLInesRibbon.Build` |
| Rafi_Pro_MOds2 | Roof Utilities | Combined Tools · Ridge Lines (By Openings V070, V069, By Points V059, V058) · Roof Types | `RoofToolsRibbon.BuildUtilities` |

### Documents (`Rafi_Pro_Docs`, `Rafi_Pro_Docs2`)

| Tab | Panel | Items | Builder |
|---|---|---|---|
| Rafi_Pro_Docs | View Create | From Lines · Around Edges · Edge Element ▸ Roof Focus · Isolated Section *button* | `ViewToolsRibbon` |
| Rafi_Pro_Docs | Floor Views | Floor Around Edges *button* · Floor Edge Element *button* · Floor Focus *button* — each V001 | `FloorToolsRibbon.BuildViewsPanel` (called from `ViewToolsRibbon`) |
| Rafi_Pro_Docs | View Place | Place Sections · Callout To Section · Head Placer · View Tagger | `ViewToolsRibbon` |
| Rafi_Pro_Docs | View Rename | Bubble Renumber · Section Renamer · Auto Renamer | `ViewToolsRibbon` |
| Rafi_Pro_Docs2 | Tagging | Tag To Text, Align Spot Elevations, Align to Detail Line (large *buttons*) · Quick Align Vertical / Horizontal · Quick Align Ends V / H · Quick Align Bends V / H · Quick Align Ends / Bends to Tag | `TaggingRibbon` |
| Rafi_Pro_Docs | Sheet Create | Scope Box · Sheet Placer · Section Placer · Sections To Sheet *button* ▸ Rearrange · Arrange Views *button* | `SheetToolsRibbon` |
| Rafi_Pro_Docs | Sheet Edit | Text Replace *button* (large) | `SheetToolsRibbon` |
| Rafi_Pro_Docs2 | DWG Tools | DWG To DL(P) · Export DWG · DWG Manager · DWG Statistics | `SetupRibbon` |
| Rafi_Pro_Docs2 | Family Tools | Batch Link DWG · DWG To Lines | `SetupRibbon` |
| Rafi_Pro_Docs2 | Manage | Overlaps · ParaManager · Shared Params *button* · Schedule I/O · Link Check (8 entries: V007, V006, then V004 and V003 scan modes) ▸ Del. Line Styles · Bulk Rename · Tracker ID | `ManageRibbon` |
| Rafi_Pro_Docs2 | Worksets | WS Browser · WS Creator · WS Renamer ▸ WS Renamer XL · Delete Workset | `ManageRibbon` |
| Rafi_Pro_Docs2 | Memory | WS Memory (large *button*) · Link Memory (large; V002, V001) | `ManageRibbon` |

The latest change was plugin 6.37.0, which added the **Floor Views** panel (Documents tab, after View Create) and
Floor EPS on Floor Edit. 6.36.0 added the **Floor Edit** panel (floor versions of Roof Types, Name
Builder, Roof Tag, Point Comparison and Elevation Sync) after Floor Lines. 6.35.0 added the **Floor Lines** panel (floor versions of Ridge By Openings,
Ridge By Points and Creaser Adv) after Floor Points. 6.34.0 added the **Floor Points** panel (floor versions of
the Shape Points tools) after Shape Points. Before it, 6.33.0 added the floor Ridge, Multi Copies and Points & Slope tools to the
**Floor Slope** panel (By Point became a pulldown, as on Roof Slope). 6.32.0 added Quick Align Ends / Bends to Tag to
the Tagging panel. When you change the ribbon, update the
tables above, `README.md` (Ribbon section) and the tool's UI preview (root `CLAUDE.md`, UI previews).

## Adding a new icon

Drop the PNG in the right `Resources/Icons/<Category>/` folder. That's all: `Revit26_Plugin_Revised.csproj` embeds
every `Resources\Icons\**\*.png` through one wildcard, with a matching `<None Remove>` so the SDK doesn't also
include it as `None`:

```xml
<None Remove="Resources\Icons\**\*.png" />
<EmbeddedResource Include="Resources\Icons\**\*.png" />
```

A typo in the resource path doesn't fail the build. It fails at runtime the first time `ImageUtils.Load` is asked
for that path, i.e. when the button is built in `OnStartup`. Names are case-sensitive.

The manifest-resource path is the `<RootNamespace>` (`Revit26_Plugin_Revised`) followed by the folders as dotted
segments: `Resources/Icons/RoofTools/by_point_16.png` on disk is
`"Revit26_Plugin_Revised.Resources.Icons.RoofTools.by_point_16.png"` in code.

## 16px vs 32px

Each tool should have both sizes: `<Name>_16.png` for `PushButtonData.Image` (a button in a stack or dropdown) and
`<Name>_32.png` for `.LargeImage` (a pulldown's own header icon, or a full-size button). Not every tool has both,
so check before assuming a `_32` exists because a `_16` does. All floor buttons reuse the roof (and roof
view-tool) icons of the tool they were ported from until floor icons are made.

## Adding a push button

```csharp
var btn = new PushButtonData("Btn_MyTool_V001", "My Tool", assemblyPath,
    "Revit26_Plugin_Revised.MyTool.V001.Commands.MyToolCommand")
{
    Image = ImageUtils.Load("Revit26_Plugin_Revised.Resources.Icons.RoofTools.MyTool_16.png"),
    ToolTip = ToolCatalog.MyTool.Tip("One-line description of what the tool does.")
};
```

- The **label** shows the tool name, never a version. Keep it to about 15 characters: a stacked column is as wide
  as its longest label, and the tooltip carries the full name.
- The **tooltip** comes from `ToolCatalog.<Tool>.Tip(detail)`, so the name and version always match the window
  title. `RibbonLayoutHelper.AddStackedButtons` and `WirePulldownButton` also refresh the first tooltip line from
  `ToolCatalog` by command class (`ToolCatalog.Find`). Older buttons still pass a
  `RibbonLayoutHelper.VersionTip(tool, version, detail)` string, which that refresh replaces.
- The **internal name** (first argument) and the **command class name** never change once released (root
  `CLAUDE.md`, Backward compatibility 1). The command class string is not compiler-checked: a typo builds fine and
  only fails when the button is clicked. After a namespace change, grep the ribbon files for the old name.
- **Set `Image` on the `PushButtonData`**, as above, rather than on the item that `AddPushButton` returns. Every
  ribbon file uses that pattern.

## Stacks, pulldowns and slide-outs — use `RibbonLayoutHelper`

`RibbonLayoutHelper` works around two Revit ribbon quirks. Skipping it brings both back:

- **Stacked-item label bug:** inside `panel.AddStackedItems(...)`, a button's `Text` is often dropped at render
  time (icon, no label). `AddStackedButtons` re-applies `ItemText` / `ToolTip` on the created items to force them
  to bind.
- **Lone large button with no `LargeImage`:** `AddStackedButtons` stacks 3 per column and splits 4 as 2 + 2. A
  leftover single item becomes a full-size `AddItem` button, which needs `LargeImage` or it shows a blank icon slot.
  The helper backfills `LargeImage = Image` for that case.

A pulldown for coexisting versions of one tool takes two calls, in order:

```csharp
var pulldownData = RibbonLayoutHelper.CreatePulldownButtonData("Pulldown_MyTool", "My Tool", highestVersion);
var created = RibbonLayoutHelper.AddStackedButtons(panel, new List<RibbonItemData> { pulldownData, ... });
RibbonLayoutHelper.WirePulldownButton(created, "Pulldown_MyTool", highestVersion, nextVersion, oldestVersion);
```

- **Order:** highest version first, every older version below it in descending order, never "current version
  first". The highest version is also the `primary` argument that gives the pulldown its icon (root `CLAUDE.md`,
  Dropdown version order).
- `WirePulldownButton` fills the dropdown and builds the combined "N versions" tooltip.
- **Use a dropdown only to group versions or different tools.** One button runs one tool. A tool with a single
  version gets a plain button, as Floor Slope's By Drain and Points & Slope do.

**Slide-outs:** `RibbonLayoutHelper.AddStackedButtonsWithSlideOut(panel, visible, slideOut)` adds the visible
stacks, calls `panel.AddSlideOut()`, then adds the slide-out stacks. It returns every created item, so
`WirePulldownButton` works unchanged. A panel has at most one slide-out. **Never add one without the owner's
agreement** on which panel and which tools go inside it (root `CLAUDE.md`, Slide-out). Current slide-outs: Shape
Points, View Create, Sheet Create, Manage, Worksets.

**Large buttons:** `panel.AddItem(...)` gives a full-size button. Use it deliberately and set `LargeImage`. Current
uses: Tagging (Tag To Text, Align Spot Elevations, Align to Detail Line), Memory (WS Memory, Link Memory), Detail
Lines (Line Manager), Plan View (Island Splitter) and Sheet Edit (Text Replace).

## Panel layout — keep the tabs narrow

Revit shrinks a tab that is wider than its window. First it drops the names of stacked buttons, starting with the
right-most panels, then it folds whole panels into one dropdown. Every button should show its icon **and** name, so:

- **Add a tool to the panel it belongs with** rather than creating a panel for it. Current one-item panels are
  Plan View and Sheet Edit. Floor Slope (6.31.0) was added as its own panel at the owner's request.
- **Short labels:** about 15 characters (see above).
- **Panel names are part of shortcut ids.** Revit keys keyboard shortcuts and Quick Access Toolbar items on
  tab + panel + button name, so moving a button to another panel, or renaming a panel, resets them. Never rename a
  panel just for its title, and note moved buttons in the CHANGELOG user note.

## Other files in this area

- `Claude/LOGO PROMPT FOLDER/Icon_Prompts.md`: icon-generation prompts. They were written for an older ribbon, and
  the Floor Slope prompts were added in 6.31.0. Extend them as buttons change.
- `Claude/LOGO PROMPT FOLDER/Ribbon_Code_Notes.md`: historical notes from the first PushButton/icon cleanup.
