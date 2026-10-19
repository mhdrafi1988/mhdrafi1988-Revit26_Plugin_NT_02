# UI/UX Patterns

How the newer/actively-maintained tool windows are actually built —
shared visual system, filtering, grouping, logging, wizards, and
multi-item tabs. These are patterns found in specific tools (named below),
not enforced repo-wide — older or untouched tools may predate them and use
something simpler. Treat this as "what to copy when building or updating a
tool's UI," not a rule every existing window already follows.

## MVVM toolkit

Newer ViewModels use **CommunityToolkit.Mvvm** (`[ObservableProperty]`,
`[RelayCommand]`, `ObservableObject`/`ObservableRecipient`) rather than
hand-written `INotifyPropertyChanged`/`ICommand`. Confirmed in
`CalloutCOPViewModel.cs`, `RoofTabViewModel.cs`, `ParaManagerViewModel.cs`.
Prefer it for any new or substantially-touched ViewModel.

## Shared visual system — `Shared/SharedStyles.xaml`

"Navy Corporate Theme", currently v3.0 per its own header comment. Color
tokens as `<Color>` resources, then a matching `<SolidColorBrush>` per
token (`NavyPrimary` → `BrushNavyPrimary`, etc.) — reference the brush, not
the raw color, from window XAML. Key groups:
- Backgrounds/text/status colors for the general card-based UI.
- A **log panel palette that's always dark**, independent of the rest of
  the window's theme (`LogBackground`, `LogText`, `LogSuccess`/`Warning`/
  `Error`, ...).
- `InputBackground` (light orange) specifically so input fields read as
  visibly distinct from card backgrounds across every tool — don't override
  this per-tool.

Root-tag brushes must be bound via `DynamicResource`, not `StaticResource`
— a `StaticResource` lookup on a pack-URI-loaded root tag broke in
`ParaManager`/`WorksetRenamer` and was fixed repo-wide (see
`Claude/CHANGELOG.md`, 2026-09-17).

## Converters — declare in the window, not in `SharedStyles.xaml`

`Shared/Converters.cs` (namespace `Revit26_Plugin.Shared.Models`) holds the
shared value converters. Its own file header states the convention
explicitly: instantiate each converter in the **consuming window's own
`Resources` block**, never inside `SharedStyles.xaml` — doing the latter
causes "incompatible type" / "does not exist in namespace" errors at
design time. Reference with the assembly-qualified xmlns:

```xml
xmlns:converters="clr-namespace:Revit26_Plugin.Shared.Models;assembly=Revit26_Plugin_Revised"
...
<converters:LogLevelToColorConverter x:Key="LogLevelToColorConverter"/>
```

Converters worth knowing before writing a new one (check this list first —
several are generic enough to reuse as-is):
- `BoolToVisibilityConverter` / `InverseBoolToVisibilityConverter` /
  `IntToVisibilityConverter` — standard visibility toggles.
- `InverseBoolConverter` — inverted `IsEnabled` bindings (e.g. disable
  while busy).
- `LogLevelToColorConverter` — maps `LogLevel` to the dark log panel's text
  color.
- `EnumToBoolConverter` — see **Quick-filter chips** below.
- `ViewTypeGroupToBrushConverter` — see **Category pills** below.
- `BoolToHeaderIconBrushConverter` — active/inactive toolbar icon tint.

Two converters (`EnumToBoolConverter`, `ViewTypeGroupToBrushConverter`)
deliberately match by `.ToString()`/member name rather than a concrete
enum type, specifically so one shared converter instance works against any
tool's own locally-declared copy of a similarly-shaped enum. Follow that
approach for a new converter that needs to work across tools whose enums
aren't (and don't need to be) unified into one shared type.

## Filtering — `ICollectionView` + a `[ObservableProperty]` filter state

Established pattern, seen in `ViewAutoRenamer_V004/ViewModels/
ViewsListViewModel.cs` and `CalloutCOP_V019/CalloutCOPViewModel.cs`:

```csharp
public ICollectionView ViewsGrid { get; }
...
ViewsGrid = CollectionViewSource.GetDefaultView(Views);
ViewsGrid.Filter = PassesAllFilters;   // one predicate combining every active filter
```

Filter *state* (a quick-filter enum, a search text box, a sheet/category
combo selection, checkbox toggles) lives as `[ObservableProperty]` fields.
Each one's generated `partial void On<Prop>Changed(...)` calls
`ViewsGrid.Refresh()` so the grid updates live as the user types/toggles —
don't wire a manual "Apply Filter" button for this; the established pattern
re-filters immediately.

**Quick-filter chips**: a row of `RadioButton`s styled as toggle "chips"
(`GroupName="QuickFilter"`, shared `ChipToggle` style), each bound
**one-way** through `EnumToBoolConverter` for its checked state and driving
the actual change through a `Command`, not two-way binding — this split is
intentional (see the converter's own doc comment) so the state change goes
through the ViewModel's command (which can also do bookkeeping like
`RecalculateQuickFilterCounts`), not an implicit two-way write:

```xml
<RadioButton GroupName="QuickFilter" Style="{StaticResource ChipToggle}"
             IsChecked="{Binding ActiveQuickFilter, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Unplaced, Mode=OneWay}"
             Command="{Binding SetQuickFilterCommand}" CommandParameter="Unplaced">
```

**Filter popovers with their own search**: `CalloutCOPViewModel` nests a
second `ICollectionView` (`SheetFilterItemsView` over `SheetFilterItems`)
inside a filter dropdown, with its own `_sheetFilterSearchText`, so a long
list of sheets/values can itself be searched before picking one — reuse
this nested-`ICollectionView` shape for any filter popover backed by a
long option list, rather than an unfiltered `ComboBox`.

**Counts alongside filters**: quick-filter and status counts
(`RecalculateQuickFilterCounts`, `SelectedCount`, `PlacedCount`, ...) are
recomputed and exposed as their own bindable properties so chip/badge
labels can show "(12)" next to a filter name — compute these next to the
filter logic, not separately in the view.

## Grouping — category pills + grouped checkbox filters

**Pills**: a small colored `Border` + `TextBlock` (`ViewTypePillBorder` /
`ViewTypePillText` styles) showing a category, background/foreground driven
by `ViewTypeGroupToBrushConverter` off a `TypeGroup`-shaped property. The
converter's palette (`SectionOrCallout`, `FloorPlan`, `CeilingPlan`,
`Elevation`, ...) is the canonical color-per-category mapping — extend it
there rather than hardcoding a category color in a new tool's XAML.

**Grouped filter checkboxes**: `ViewAutoRenamerWindow`'s
`ObservableCollection<ViewTypeFilterGroup> ViewTypeFilterGroups` — each
group has a category header and a nested list of checkbox rows
(`BuildViewTypeFilterGroups`) — is the pattern for "filter by category,
with per-item overrides within it" (as opposed to the flat quick-filter
chips above, which are mutually exclusive). Use grouped checkboxes when the
filter dimension has many values that cluster into a handful of categories;
use quick-filter chips when there are only a few mutually-exclusive states.

## Logging — shared `LogEntry`/`LogLevel`, always-dark panel

`Shared/LogEntry.cs` / `Shared/LogLevel.cs` (`Info`, `Warning`, `Error`,
`Success`, `Debug`) are explicitly the single canonical log model —
`LogEntry.cs`'s own header says "Replaces per-tool copies; reference only
this class going forward." Render log rows through
`LogLevelToColorConverter` against the dark `Log*` brushes from
`SharedStyles.xaml`, regardless of whether the rest of the window is light
— the log panel is a fixed dark surface by design, not theme-following.
`LogEntry.ToString()` already formats as `HH:mm:ss  Level  Message` for a
"Copy Log" / clipboard-export action — use it rather than reformatting.

## Wizard / multi-step windows

`ParaManager_V003`'s pattern for a linear wizard: an int `CurrentStep`
`[ObservableProperty]`, bool `IsStep1`..`IsStepN` computed properties
manually raised from `OnCurrentStepChanged` (needed because
`[ObservableProperty]` won't auto-derive them), and `[RelayCommand]`
Next/Back commands gated by `CanExecute` (`CanGoNext` validates the
*current* step before allowing advance; `CanGoBack` just checks
`CurrentStep > 1`). Bind each step's panel visibility to its `IsStepN`
property (via `BoolToVisibilityConverter`).

## Multi-item tabs

`AutoSlopeByDrain_V010`'s multi-roof UI: an
`ObservableCollection<RoofTabViewModel> RoofTabs`, one `RoofTabViewModel`
instance per picked roof, each bound to one `TabItem`. Use this shape (a
collection of per-item child ViewModels feeding a `TabControl`) whenever a
tool moves from single-element to multi-element selection — it's the
established alternative to cramming a picked-count loop into one flat
ViewModel, and is what `Claude/ARCHITECTURE.md` §5's batch-processing
pattern pairs with on the ViewModel side.
