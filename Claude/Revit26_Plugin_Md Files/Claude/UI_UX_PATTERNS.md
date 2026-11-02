# UI/UX Patterns

How the newer and actively maintained tool windows are built: shared visual system, window shell, filtering,
grouping, logging, wizards and multi-item tabs. Updated 2026-11-02 (plugin 6.31.0). These patterns come from
specific tools (named below), and are not enforced repo-wide. Older tools may predate them. Treat this as "what to
copy when building or updating a tool's UI".

Every tool update also needs an HTML preview of the window in `Claude/Previews/`, published as a claude.ai Artifact
(root `CLAUDE.md`, UI previews). The preview uses the same colour tokens as `Shared/SharedStyles.xaml`.

## Window shell

Every tool window's content is a `Shared/Controls/ToolWindowShell`, with a fixed header, one scrolling body and a
fixed footer. The rules (no nested `ScrollViewer`, `ShellBehaviors.MaxHeightRatio` on grids and logs, tokens only,
title from `ToolCatalog`) are in `CLAUDE.md` next to this folder. Good examples: Section Auto Renamer V026, Auto
Slope By Drain V013, Floor Slope By Drain V001.

## MVVM toolkit

Newer ViewModels use **CommunityToolkit.Mvvm** (`[ObservableProperty]`, `[RelayCommand]`, `ObservableObject`)
rather than hand-written `INotifyPropertyChanged` / `ICommand`. Examples: `CalloutCOPViewModel.cs` (Callout To
Section V021), `RoofTabViewModel.cs` (Auto Slope By Drain V013), `ParaManagerViewModel.cs` (ParaManager V005). Many
tools derive from `Shared/ViewModels/ToolViewModelBase`, which provides the log collection, log commands and shell
properties (26 ViewModels outside WIP use it). Use both for a new or substantially changed ViewModel.

## Shared visual system — `Shared/SharedStyles.xaml`

The "Navy Corporate Theme". Colour tokens are `<Color>` resources, each with a matching `<SolidColorBrush>`
(`NavyPrimary` → `BrushNavyPrimary`, etc.). Reference the brush, not the raw colour, from window XAML. New and
migrated XAML uses the **v4.0 semantic tokens**. Key groups:

- Backgrounds, text and status colours for the card-based UI, plus tinted metric tiles (v3.1) and pills
  (`PillInfo…`, `PillSuccess…`, `PillMuted…`).
- A **log panel palette that is always dark** (`LogBackground` `#14283F`, `LogText`, `LogSuccess` / `Warning` /
  `Error` …), whatever the rest of the window looks like.
- `InputBackground`, now `#FFFFFF`. Its comment in the file still says "light orange". Don't override it per tool.

No inline colours in window XAML; `Transparent` is the one exception. Add a token, or a style, to
`SharedStyles.xaml` instead.

Root-tag brushes must be bound with `DynamicResource`, not `StaticResource`. A `StaticResource` lookup on a
pack-URI-loaded root tag broke in ParaManager and Workset Renamer and was fixed repo-wide (see `Claude/CHANGELOG.md`,
2026-09-17).

## Converters — declare in the window, not in `SharedStyles.xaml`

`Shared/Converters.cs` (namespace `Revit26_Plugin_Revised.Shared.Models`) holds the shared value converters.
Instantiate each converter in the **consuming window's own `Resources`**, never inside `SharedStyles.xaml`. Doing
the latter causes "incompatible type" / "does not exist in namespace" errors at design time. Reference them with the
assembly-qualified xmlns:

```xml
xmlns:converters="clr-namespace:Revit26_Plugin_Revised.Shared.Models;assembly=Revit26_Plugin_Revised"
...
<converters:LogLevelToColorConverter x:Key="LogLevelToColorConverter"/>
```

Check this list before writing a new converter:
- `BoolToVisibilityConverter` / `InverseBoolToVisibilityConverter` / `IntToVisibilityConverter`: visibility toggles.
- `InverseBoolConverter`: inverted `IsEnabled` bindings (e.g. disable while busy).
- `LogLevelToColorConverter`: maps `LogLevel` to the dark log panel's text colour.
- `EnumToBoolConverter`: see **Quick-filter chips** below.
- `ViewTypeGroupToBrushConverter`: see **Category pills** below.
- `BoolToHeaderIconBrushConverter`: active/inactive toolbar icon tint.
- `MixedWorksetToColorConverter`: maps a bool to orange (true) or dark grey (false), e.g. to highlight mixed
  worksets or another status flag.

`EnumToBoolConverter` and `ViewTypeGroupToBrushConverter` match by `.ToString()` / member name rather than a
concrete enum type. That way one shared converter works against each tool's own copy of a similar enum. Do the same
for a new converter that has to work across tools whose enums aren't unified.

## Filtering — `ICollectionView` plus observable filter state

The established pattern is in `Menu/ViewTools/Rename/ViewAutoRenamer_V007/ViewModels/ViewsListViewModel.cs` and
`Menu/ViewTools/Place/CalloutCOP_V021/CalloutCOPViewModel.cs`:

```csharp
public ICollectionView ViewsGrid { get; }
...
ViewsGrid = CollectionViewSource.GetDefaultView(Views);
ViewsGrid.Filter = PassesAllFilters;   // one predicate combining every active filter
```

Filter state (a quick-filter enum, a search box, a combo selection, checkbox toggles) lives in `[ObservableProperty]`
fields. Each one's generated `partial void On<Prop>Changed(...)` calls `ViewsGrid.Refresh()`, so the grid updates
as the user types. Don't add a manual "Apply Filter" button.

**Quick-filter chips:** a row of `RadioButton`s styled as chips (`GroupName="QuickFilter"`, shared `ChipToggle`
style). Each one's checked state is bound **one-way** through `EnumToBoolConverter`, and the change goes through a
`Command`, not a two-way binding. That lets the ViewModel's command do bookkeeping such as
`RecalculateQuickFilterCounts`:

```xml
<RadioButton GroupName="QuickFilter" Style="{StaticResource ChipToggle}"
             IsChecked="{Binding ActiveQuickFilter, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Unplaced, Mode=OneWay}"
             Command="{Binding SetQuickFilterCommand}" CommandParameter="Unplaced">
```

**Filter popovers with their own search:** `CalloutCOPViewModel` nests a second `ICollectionView`
(`SheetFilterItemsView` over `SheetFilterItems`) inside a filter dropdown, with its own `_sheetFilterSearchText`,
so a long list of sheets can be searched before picking one. Reuse this shape for any filter popover with a long
option list, rather than an unfiltered `ComboBox`.

**Counts alongside filters:** counts such as `RecalculateQuickFilterCounts`, `SelectedCount` and `PlacedCount` are
exposed as their own bindable properties, so chip and badge labels can show "(12)". Compute them next to the filter
logic, not in the view.

## Grouping — category pills and grouped rows

**Category pills:** a small coloured `Border` + `TextBlock` (`ViewTypePillBorder` / `ViewTypePillText`, declared in
`ViewAutoRenamerWindow.xaml`) showing a category, coloured by `ViewTypeGroupToBrushConverter` from a
`TypeGroup`-shaped property. The converter's palette (`SectionOrCallout`, `FloorPlan`, `CeilingPlan`, `Elevation`,
…) is the colour-per-category mapping. Extend it there rather than hard-coding a colour in a new tool.

**Grouped filter checkboxes:** View Auto Renamer's `ViewTypeFilterGroups` (built by `BuildViewTypeFilterGroups`):
each group has a category header and nested checkbox rows. Use it for "filter by category, with per-item
overrides". Use the quick-filter chips above when there are only a few mutually exclusive states.

**Grouped grid rows:** the drains grid in Auto Slope By Drain V013 and Floor Slope By Drain V001 groups its
`ICollectionView` with `PropertyGroupDescription(nameof(DrainItem.ShapeGroup))`, sorted by a `ShapeGroupOrder`
property. Each group header has All / None buttons bound to `SelectAllInGroupCommand` / `SelectNoneInGroupCommand`
with the group name as parameter. Floor Slope adds a "Fixture" group for drain families.

## Logging — shared `LogEntry` / `LogLevel`, always-dark panel

`Shared/LogEntry.cs` and `Shared/LogLevel.cs` (`Info`, `Warning`, `Error`, `Success`, `Debug`) are the one log
model. The file header says "Replaces per-tool copies; reference only this class going forward." Render log rows
through `LogLevelToColorConverter` against the dark `Log*` brushes. The log lives in the shell body, and newer tools
open it collapsed. `LogEntry.ToString()` formats as `HH:mm:ss  Level  Message`. Use `LogClipboardService` (Copy All /
Copy Selected) and `LogExportService` (Save log…) rather than reformatting.

## Wizard / multi-step windows

ParaManager V005's pattern for a linear wizard:
- an int `CurrentStep` `[ObservableProperty]`;
- bool `IsStep1`..`IsStepN` computed properties, raised by hand from `OnCurrentStepChanged`, because
  `[ObservableProperty]` won't derive them;
- `[RelayCommand]` Next / Back commands gated by `CanExecute`. `CanGoNext` validates the current step, and
  `CanGoBack` checks `CurrentStep > 1`.

Bind each step panel's visibility to its `IsStepN` property through `BoolToVisibilityConverter`.

## Multi-item tabs

Auto Slope By Drain V013's multi-roof UI: an `ObservableCollection<RoofTabViewModel> RoofTabs`, one child
ViewModel per picked roof, each bound to one `TabItem`. Floor Slope By Drain V001 does the same with
`FloorTabViewModel`. Use this shape (a collection of per-item child ViewModels feeding a `TabControl`) whenever a
tool moves from one element to several. It pairs with the batch pattern in `Claude/ARCHITECTURE.md` §5 on the
model side.
