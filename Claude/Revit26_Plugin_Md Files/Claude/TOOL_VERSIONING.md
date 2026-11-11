# Tool Versioning

What a tool's version means in this repo, the states a tool folder can be in, and how versions change and retire.
Updated 2026-11-03 (plugin 6.37.0). The rules are in the root `CLAUDE.md` (Repository conventions, Documentation,
Backward compatibility). This file explains them and the history behind them.

## Three version numbers

| Number | Where | Example | Meaning |
|---|---|---|---|
| Plugin version | `<Version>` in `Revit26_Plugin_Revised.csproj` | `6.37.0` | The release. Semantic versioning, one CHANGELOG entry per release. |
| Tool version | `Shared/Services/ToolCatalog.cs` | `Add("Floor Slope By Drain", "1.0.0", "V001", "Revit26_Plugin_Revised.FloorSlopeByDrain.V001")` | Each tool's own semantic version. The window title (`Name — vX.Y.Z`), the ribbon tooltip (`… (build V001)`) and error dialogs read it. |
| Build tag | The folder / namespace suffix | `_V001`, `VA010`, `WSEB004`, `FX05`, `MultiCopies30` | Identifies the code lineage. It is the third `ToolCatalog` argument and is not a semantic version. |

`ToolCatalog.Find(type)` maps any class to its tool through the namespace prefix (the fourth argument). A tool that
has no entry falls back to a hard-coded version, so **every tool, including each coexisting build, needs its own
entry**.

Build tags are not consistently padded (`Btn_RoofRidgeLines_V69` and `V70` next to `_V013`), so don't sort folders
by name to find the newest. Read the ribbon file: a pulldown lists its versions highest first.

## How a tool changes now

Since plugin 3.10.0 (2026-10-19), **git is the version history**:

- **Change the tool in place and bump its `ToolCatalog` version.** MAJOR is for a breaking change or removal,
  MINOR for a new feature, PATCH for a fix. Bump the plugin version too, and add a CHANGELOG entry.
- **Never copy a tool folder to make a new version** (`Tool_V005` next to `Tool_V004`), and never commit `.zip`
  backups. The only `.zip` allowed is a shareable tool package made with `git archive` from `main`, in
  `Installer/Output/ToolPackages/`.
- **A new tool** gets its own folder at `_V001`, a new `ToolCatalog` entry and a new ribbon button. For example, the
  floor versions of the roof slope tools in 6.31.0 are new tools (`FloorSlopeByDrain_V001`,
  `FloorSlopeByPoint_V001`), not new versions of the roof tools.
- **Every tool update** also updates the tool's HTML UI preview in `Claude/Previews/` and republishes it as a
  claude.ai Artifact (root `CLAUDE.md`, UI previews).

Before 3.10.0, a behaviour change usually meant a new `_V0xx` folder, wired into the ribbon next to the old one so
users could fall back. That is why many tools still have two or more coexisting folders (`AutoSlopeByPoint_V030`,
`_V032`, `_V033`, `_V034`; V034 was added in 6.47.0 at the owner's request). They stay until they are retired as below.

## Floor versions of roof tools (plugins 6.31.0–6.37.0)

Every floor tool is a **new tool at `_V001`**, ported from the **latest roof version only**. Older roof builds
(for example Ridge By Openings V069 next to V070) are not ported and get no floor button. The roof folders are
not changed; each floor tool is a copy with its own namespace, `ToolCatalog` entry, settings folder and ribbon
button. When the roof tool gets a new version later, port the change to the floor tool in place and bump the
floor tool's own version. Don't add a second floor folder.

| Floor tool (1.0.0, V001) | Ported from | Folder |
|---|---|---|
| Floor Slope By Drain | Auto Slope By Drain (Multi-Roof) V013 | `Menu/FloorTools/FloorSlope/ByDrains/FloorSlopeByDrain_V001` |
| Floor Slope By Point | Auto Slope By Point V033 | `Menu/FloorTools/FloorSlope/ByPoints/FloorSlopeByPoint_V001` |
| Floor Slope By Point (Ridge) | Auto Slope By Point (Ridge) V003 | `Menu/FloorTools/FloorSlope/ByPoints/FloorSlopeByPointRidge_V001` |
| Floor Slope By Point (Multi Copies) | Auto Slope By Point (Multi Copies) 30 | `Menu/FloorTools/FloorSlope/ByPoints/FloorSlopeByPointMultiCopies_V001` |
| Floor Points & Slope | Roof Points & Slope V001 | `Menu/FloorTools/CombinedTools/FloorPointsAndSlope_V001` |
| Floor Divide Inner Loops | Divide Inner Loops V012 | `Menu/FloorTools/Points/FloorInnerLoopDivider_V001` |
| Floor Outer Curve Divider | Outer Curve Divider V006 | `Menu/FloorTools/Points/FloorOuterCurveDivider_V001` |
| Floor Detail Line Intersect | Roof Detail Line Intersect V014 | `Menu/FloorTools/Points/FloorDetailLineIntersect_V001` |
| Floor Inner Loops And Perpendicular | Inner Loops And Perpendicular V007 | `Menu/FloorTools/Points/FloorInnerLoopsAndPerpendicular_V001` |
| Floor Edge Vertex Reducer | Roof Edge Vertex Reducer V009 | `Menu/FloorTools/Points/FloorEdgeVertexReducer_V001` |
| Floor Multiple Points (1/2–1/4) | Multiple Points V003 | `Menu/FloorTools/Points/FloorMultiplePoints_V001` |
| Floor Shape From Link | Roof Shape From Link V002 | `Menu/FloorTools/Points/FloorShapeFromLink_V001` |
| Floor Crease Lines By Openings | Ridge By Openings V070 | `Menu/FloorTools/Lines/FloorCreaseLinesByOpenings_V001` |
| Floor Crease Lines By Points | Ridge By Points V059 | `Menu/FloorTools/Lines/FloorCreaseLinesByPoints_V001` |
| Floor Creaser | Creaser Adv V012 | `Menu/FloorTools/Lines/FloorCreaser_V001` |
| Floor Type Manager | Roof Type Manager V003 | `Menu/FloorTools/Types/FloorTypeManager_V001` |
| Floor Name Builder | Roof Name Builder V002 | `Menu/FloorTools/Types/FloorNameBuilder_V001` |
| Floor Tag | Roof Tag V018 | `Menu/Tagging/FloorTag_V001` |
| Floor Point Comparison | Roof Point Comparison V003 | `Menu/FloorTools/Compare/FloorPointComparison_V001` |
| Floor Point Elevation Sync | Roof Point Elevation Sync V005 | `Menu/FloorTools/Compare/FloorPointElevationSync_V001` |
| Floor Edge Around Sections | Roof Edge Around Sections V007 | `Menu/ViewTools/Create/FloorEdgeAroundSections_V001` |
| Floor Edge Element Sections | Roof Edge Element Sections V004 | `Menu/ViewTools/Create/FloorEdgeElementSections_V001` |
| Floor View Focus | Roof View Focus V004 | `Menu/ViewTools/Create/FloorViewFocus_V001` |
| Floor Tapered EPS 2D Pattern | Tapered EPS 2D Pattern V006 | `Menu/FloorTools/Modelling/FloorTaperedEpsPattern_V001` |

Not ported (no floor equivalent needed or not requested yet): Island Roof Splitter, Combined Roof Tools, Roof From
Detail Lines. Each floor tool's folder has a `README_V001.md` with the differences from its roof tool.

## A higher build is not always a replacement

Some pulldowns hold genuinely different implementations, not generations of one tool. **Roof Slope ▸ By Point**
lists V034, V033, V032 and V030 (one lineage), plus **Ridge V003** (ridge-aware) and **Multi Copies 30** (multi-copy).
Before assuming the highest build is "the" tool and the rest are dead weight, check whether the ribbon groups them
as alternatives.

## States a tool folder can be in

1. **Live:** a ribbon file references its command class by fully-qualified name. Look in
   `Menu/00_Push_Button_Menu_items/Ribbon/*.cs`; there is no `QuickAcces.cs` any more.
2. **Compiled, not on the ribbon:** this is an SDK-style project, so every `.cs` under the project root compiles
   unless excluded. A folder with no ribbon reference still builds into the assembly, and other tools may use its
   code. Check the ribbon files, not the `.csproj`, to know whether a tool is reachable.
3. **Work in progress:** `Menu/RoofTools/WIP/` is excluded from the build (`<Compile Remove="Menu\RoofTools\WIP\**" />`
   and the matching `EmbeddedResource` / `None` / `Page` removes). The only other excluded folder is `Tests/`.
   Single excluded files are `IconLoader.cs` and `Resources/App.xaml(.cs)`.
4. **Removed:** deleted with `git rm`, so history keeps it. Examples: the buttons whose source was missing (plugin
   4.2.1), Roof From Detail Lines V008/V009, and 48 `.zip` backups (both 3.10.0).

## Retiring a version

The root `CLAUDE.md` says to **deprecate before deleting**: keep the old behaviour for one or two releases with a
warning, then remove it. To retire a coexisting build:

1. Remove its button from the pulldown, keeping the others highest first, and note it in the CHANGELOG with a user
   note. Keyboard shortcuts on that button are lost.
2. Search the repo for its namespace (`Revit26_Plugin_Revised.<Tool>.V<NNN>`) to make sure no other tool compiles
   against it.
3. `git rm` the folder, build with 0 errors, and keep its `ToolCatalog` entry if old logs still name it.

Never change a released button's internal name, command class name or the add-in GUID (root `CLAUDE.md`, Backward
compatibility 1).
