# Per-target Callout Option — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each section target in Wet Area Waterproofing can be a reference section (existing) or a callout (new), so element-specific cuts show real model geometry without manual drawing.

**Architecture:** Add a `SectionMode` enum. Each `SectionTargetSetting` gets a `Mode` property. The planner carries it through to `PlannedSection`. ViewCreator forks at placement time: reference sections use the existing `CreateReferenceSection` path; callouts use `ViewSection.CreateCallout` to create a cropped detail view. A project-wide detail view type dropdown (like the existing plan type dropdown) controls the callout's `ViewFamilyType`.

**Tech Stack:** .NET 8, Revit 2026 API (`ViewSection.CreateCallout`), WPF/XAML, CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-19-callout-option-design.md`

## Global Constraints

- Build must pass: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` — 0 errors.
- Tests must pass: `dotnet test Tests/WetAreaWaterproofing.Tests/`.
- Root namespace: `Revit26_Plugin_Revised`.
- Enum values are stored in JSON: append only, never renumber.
- Old settings files (without `Mode` or `CalloutViewTypeName`) must load unchanged — missing fields get defaults.
- New columns in the Excel report go at the end of existing column lists.
- `ToolCatalog` version bump to `1.2.0`.
- XML doc comments on every new public member.
- `CHANGELOG.md` entry.
- HTML preview updated and published as an Artifact.

## Review Focus

1. **Old settings file with no `Mode` field** — must deserialize to `SectionMode.ReferenceSection` (the `= 0` default). Covered by existing `NormalizeRestoresMissingTargetsAndKeepsOrder` test being extended.
2. **Project with no detail view type** — `CheckReady` must catch this when any target is Callout mode and return a readable error. Tested indirectly via the preflight check in `RevitWetAreaSession.RunCore`.
3. **All targets set to Callout + `OneDraftingViewForAll = true`** — callouts ignore the drafting view name; this combination must not create empty drafting views. The planner sets `DraftingViewName` to empty for callouts; ViewCreator skips `EnsureDrafting` for them.
4. **Mixed modes in one room** — some targets reference section, some callout. ViewCreator handles both in one `PlaceSections` call. The report must list each with its mode.
5. **`CreateCallout` with head==tail or zero-area box** — Revit throws. The planner already guarantees nonzero-length cuts, and `CalloutMarginMm` (default 300) expands them to a rectangle. A test pins this.

---

### Task 1: Enum, model and planner changes (pure logic, no Revit)

**Files:**
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Core/Models/Enums.cs`
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Core/Sections/SectionTargets.cs` (class `SectionTargetSetting`, static `Normalize`)
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Core/Sections/SectionPlanner.cs` (record `PlannedSection`)
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Core/Models/WetAreaSettings.cs`
- Test: `Tests/WetAreaWaterproofing.Tests/SectionPlannerTests.cs`

**Interfaces:**
- Produces: `SectionMode` enum (`ReferenceSection = 0, Callout = 1`)
- Produces: `SectionTargetSetting.Mode` property (default `SectionMode.ReferenceSection`)
- Produces: `PlannedSection.Mode` field (carried from `SectionTargetSetting.Mode`)
- Produces: `WetAreaSettings.CalloutViewTypeName` (string, default empty)
- Produces: `WetAreaSettings.CalloutScale` (int, default 20)
- Produces: `WetAreaSettings.CalloutMarginMm` (double, default 300)

- [ ] **Step 1: Write failing tests**

Add to `SectionPlannerTests.cs`:

```csharp
[Fact]
public void DefaultTargetModeIsReferenceSection()
{
    var s = new WetAreaSettings().Normalize();
    Assert.All(s.Targets, t => Assert.Equal(SectionMode.ReferenceSection, t.Mode));
}

[Fact]
public void CalloutModeIsCarriedThroughToPlannedSection()
{
    var targets = Targets((SectionTargetKind.FullRoomH, false));
    targets[0].Mode = SectionMode.Callout;
    var plan = Plan(targets);
    Assert.Equal(SectionMode.Callout, Assert.Single(plan.Sections).Mode);
}

[Fact]
public void CalloutSectionHasEmptyDraftingViewName()
{
    var targets = Targets((SectionTargetKind.Door, false));
    targets.First(t => t.Kind == SectionTargetKind.Door).Mode = SectionMode.Callout;
    var plan = Plan(targets, new[] { Item(SectionTargetKind.Door, 2000, -100, 77) });
    Assert.Empty(Assert.Single(plan.Sections).DraftingViewName);
}

[Fact]
public void NormalizePreservesCalloutModeFromSettings()
{
    var s = new WetAreaSettings
    {
        Targets = new List<SectionTargetSetting>
        {
            new() { Kind = SectionTargetKind.Door, On = true, LengthMm = 1500, Mode = SectionMode.Callout }
        }
    }.Normalize();
    Assert.Equal(SectionMode.Callout, s.Targets.First(t => t.Kind == SectionTargetKind.Door).Mode);
    // The other targets appended by Normalize get the default
    Assert.All(s.Targets.Where(t => t.Kind != SectionTargetKind.Door), t => Assert.Equal(SectionMode.ReferenceSection, t.Mode));
}

[Fact]
public void CalloutSettingsHaveDefaults()
{
    var s = new WetAreaSettings().Normalize();
    Assert.Equal(string.Empty, s.CalloutViewTypeName);
    Assert.Equal(20, s.CalloutScale);
    Assert.Equal(300, s.CalloutMarginMm);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Tests/WetAreaWaterproofing.Tests/ --filter "DefaultTargetModeIsReferenceSection|CalloutModeIsCarriedThroughToPlannedSection|CalloutSectionHasEmptyDraftingViewName|NormalizePreservesCalloutModeFromSettings|CalloutSettingsHaveDefaults" -v n`
Expected: FAIL — `SectionMode` does not exist.

- [ ] **Step 3: Add `SectionMode` to `Enums.cs`**

Append after the `RoomStatus` enum:

```csharp
/// <summary>How a section target is placed on the room's plan.</summary>
public enum SectionMode
{
    /// <summary>A reference section pointing to a drafting view.</summary>
    ReferenceSection = 0,
    /// <summary>A callout that creates a cropped detail view of the model.</summary>
    Callout = 1
}
```

- [ ] **Step 4: Add `Mode` to `SectionTargetSetting` in `SectionTargets.cs`**

Add property:

```csharp
/// <summary>Reference section (points to a drafting view) or callout (creates a detail view).</summary>
public SectionMode Mode { get; set; } = SectionMode.ReferenceSection;
```

No change to `Normalize` — `Mode` defaults to 0 (`ReferenceSection`) when missing from JSON, and any value that isn't 0 or 1 is harmless (cast from int).

- [ ] **Step 5: Add `Mode` to `PlannedSection` record in `SectionPlanner.cs`**

Add `SectionMode Mode` as the last parameter of the `PlannedSection` record.

- [ ] **Step 6: Carry `Mode` through in `SectionPlanner.Plan`**

In the loop that creates `PlannedSection` instances, pass `target.Mode`. When `target.Mode == SectionMode.Callout`, pass `string.Empty` for `DraftingViewName` instead of the resolved view name.

- [ ] **Step 7: Add callout settings to `WetAreaSettings`**

```csharp
/// <summary>Detail view family type name used for callout sections; empty = the first detail type in the project.</summary>
public string CalloutViewTypeName { get; set; } = string.Empty;

/// <summary>Scale of callout detail views created by the tool.</summary>
public int CalloutScale { get; set; } = 20;

/// <summary>Margin around the cut line that defines the callout's crop box, mm.</summary>
public double CalloutMarginMm { get; set; } = 300;
```

Add normalization in `Normalize()`:
```csharp
if (CalloutScale < 1 || CalloutScale > 5000) CalloutScale = 20;
if (!(CalloutMarginMm >= 0) || CalloutMarginMm > 10000) CalloutMarginMm = 300;
```

- [ ] **Step 8: Run tests**

Run: `dotnet test Tests/WetAreaWaterproofing.Tests/ -v n`
Expected: ALL PASS.

- [ ] **Step 9: Build**

Run: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true`
Expected: 0 errors.

- [ ] **Step 10: Commit**

```bash
git add Menu/Sheet/WetAreaWaterproofing_V001/Core/Models/Enums.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/Core/Sections/SectionTargets.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/Core/Sections/SectionPlanner.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/Core/Models/WetAreaSettings.cs \
       Tests/WetAreaWaterproofing.Tests/SectionPlannerTests.cs
git commit -m "feat(wet-area): add SectionMode enum and callout settings to planner

Per-target Mode (ReferenceSection or Callout) on SectionTargetSetting,
carried through PlannedSection. Callout targets get empty DraftingViewName.
WetAreaSettings gains CalloutViewTypeName, CalloutScale, CalloutMarginMm."
```

---

### Task 2: ViewCreator — callout placement path

**Files:**
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/ViewCreator.cs`
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/RoomCollector.cs` (ModelSnapshot)
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/IWetAreaSession.cs` (RunOutcome)
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/RevitWetAreaSession.cs`
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Core/Report/RunReport.cs` (CreatedSection)

**Interfaces:**
- Consumes: `PlannedSection.Mode` (from Task 1)
- Consumes: `WetAreaSettings.CalloutViewTypeName`, `.CalloutScale`, `.CalloutMarginMm` (from Task 1)
- Produces: `ViewCreator.PlaceCallout(ViewPlan, PlannedSection, string roomKey, List<string> problems) → CreatedSection?`
- Produces: `ViewCreator.CalloutsCreated` (int)
- Produces: `ModelSnapshot.DetailViewTypes` (`IReadOnlyList<string>`)
- Produces: `RunOutcome.CalloutsCreated` (int)
- Produces: `CreatedSection.IsCallout` (bool), `.CalloutViewId` (long), `.CalloutViewName` (string)

- [ ] **Step 1: Add `DetailViewTypes` to `ModelSnapshot` in `RoomCollector.cs`**

Add property:
```csharp
/// <summary>Detail view family type names.</summary>
public IReadOnlyList<string> DetailViewTypes { get; init; } = Array.Empty<string>();
```

In `RoomCollector.Read`, collect them the same way `PlanTypes` are collected, filtering `ViewFamily.Detail`:
```csharp
DetailViewTypes = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
    .Cast<ViewFamilyType>().Where(t => t.ViewFamily == ViewFamily.Detail)
    .Select(t => t.Name).OrderBy(n => n).ToList(),
```

- [ ] **Step 2: Add `CalloutsCreated` to `RunOutcome` in `IWetAreaSession.cs`**

```csharp
/// <summary>Callout detail views this run created.</summary>
public int CalloutsCreated { get; init; }
```

- [ ] **Step 3: Extend `CreatedSection` in `RunReport.cs`**

Change the record to add optional callout fields (backward compatible — existing code passes 0/null for them):

```csharp
/// <summary>A section the run placed (reference section or callout).</summary>
public sealed record CreatedSection(
    PlannedSection Plan, long MarkerId, long DraftingViewId, string DraftingViewName,
    bool IsCallout = false, long CalloutViewId = 0, string CalloutViewName = "");
```

- [ ] **Step 4: Add callout logic to `ViewCreator`**

Add a `DetailType()` private method (like `DraftingType()` but for `ViewFamily.Detail`).

Add a `_calloutsCreated` counter and a `CalloutsCreated` property.

Update `CheckReady`: when `needsCallouts` is true (new bool parameter), check that `DetailType()` is not null.

Add `PlaceCallout` method:
```csharp
/// <summary>
/// Places a callout on <paramref name="plan"/> for <paramref name="section"/>.
/// Returns the created section info, or null with a message in <paramref name="problems"/>.
/// </summary>
private CreatedSection PlaceCallout(ViewPlan plan, PlannedSection section, string roomKey, List<string> problems)
```

Logic:
1. Compute the callout's two corner points from `section.Head` and `section.Tail` + `_s.CalloutMarginMm` to form a rectangle perpendicular to the cut line.
2. Call `ViewSection.CreateCallout(_doc, plan.Id, DetailType().Id, corner1, corner2)`.
3. Name the resulting view using `ViewNames.Unique` with a pattern like `"WP Callout - {Number} - {Label}"`.
4. Set scale to `_s.CalloutScale`.
5. Tag the marker with `ElementTag.Set`.
6. Return `new CreatedSection(section, markerId, 0, "", isCallout: true, calloutViewId, calloutViewName)`.

Update `PlaceSections` to check `section.Mode`: if `Callout`, call `PlaceCallout`; if `ReferenceSection`, use the existing `CreateReferenceSection` path.

- [ ] **Step 5: Update `CheckReady` call site in `RevitWetAreaSession`**

Pass a `needsCallouts` flag: `s.Targets.Any(t => t.On && t.Mode == SectionMode.Callout)`.

- [ ] **Step 6: Pass `CalloutsCreated` in `RunOutcome`**

In `RevitWetAreaSession.RunCore`, after the loop, set:
```csharp
CalloutsCreated = viewCreator?.CalloutsCreated ?? 0,
```

- [ ] **Step 7: Build**

Run: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true`
Expected: 0 errors.

- [ ] **Step 8: Commit**

```bash
git add Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/ViewCreator.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/RoomCollector.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/IWetAreaSession.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/Infrastructure/RevitWetAreaSession.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/Core/Report/RunReport.cs
git commit -m "feat(wet-area): ViewCreator callout placement via CreateCallout

ViewCreator forks on PlannedSection.Mode: ReferenceSection uses existing
CreateReferenceSection path, Callout uses ViewSection.CreateCallout with
a detail view type from settings. ModelSnapshot collects DetailViewTypes.
RunOutcome reports CalloutsCreated."
```

---

### Task 3: ViewModel and XAML — UI for the Mode column and detail view type

**Files:**
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/UI/ViewModels/ViewModel_WetAreaWaterproofing.Views.cs` (`TargetRowViewModel`, `ViewModel_WetAreaWaterproofing`)
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/UI/Views/WetAreaWaterproofingWindow.xaml`
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/UI/ViewModels/ViewModel_WetAreaWaterproofing.Run.cs` (result display)

**Interfaces:**
- Consumes: `SectionMode` enum, `SectionTargetSetting.Mode` (Task 1)
- Consumes: `ModelSnapshot.DetailViewTypes` (Task 2)
- Consumes: `RunOutcome.CalloutsCreated` (Task 2)
- Produces: `TargetRowViewModel.Mode` (bound to ComboBox in the target row)
- Produces: `TargetRowViewModel.ModeChoices` (static list: "Reference section", "Callout")
- Produces: `ViewModel.DetailViewTypes` (ObservableCollection, populated from snapshot)
- Produces: `ViewModel.SelectedDetailViewType` (string, bound to dropdown)
- Produces: `ViewModel.AnyCalloutTarget` (bool, shows/hides the detail type dropdown)

- [ ] **Step 1: Add `Mode` to `TargetRowViewModel`**

Add static choices list:
```csharp
public static IReadOnlyList<string> ModeChoices { get; } = new[] { "Reference section", "Callout" };
```

Add observable property:
```csharp
[ObservableProperty] private string _mode;
```

Initialize from setting in constructor:
```csharp
_mode = setting.Mode == SectionMode.Callout ? ModeChoices[1] : ModeChoices[0];
```

Add change notification:
```csharp
partial void OnModeChanged(string value) => _changed?.Invoke();
```

Update `ToSetting()` to include Mode:
```csharp
Mode = Mode == ModeChoices[1] ? SectionMode.Callout : SectionMode.ReferenceSection,
```

- [ ] **Step 2: Add detail view type list and callout settings to the ViewModel**

In `ViewModel_WetAreaWaterproofing.Views.cs`:

```csharp
public ObservableCollection<string> DetailViewTypes { get; } = new();
[ObservableProperty] private string _selectedDetailViewType;
[ObservableProperty] private string _calloutScaleText;
[ObservableProperty] private string _calloutMarginText;
[ObservableProperty] private bool _anyCalloutTarget;
```

In `InitViews()`, add:
```csharp
_calloutScaleText = _settings.CalloutScale.ToString(CultureInfo.CurrentCulture);
_calloutMarginText = Text(_settings.CalloutMarginMm);
```

In `ApplyViewLists()`, add (after the DraftingViews block):
```csharp
Replace(DetailViewTypes, snapshot.DetailViewTypes);
string wantedDetail = SelectedDetailViewType ?? _settings.CalloutViewTypeName;
SelectedDetailViewType = (wantedDetail.Length > 0 && DetailViewTypes.Contains(wantedDetail))
    ? wantedDetail : DetailViewTypes.FirstOrDefault();
```

In `OnTargetsChanged()`, add:
```csharp
AnyCalloutTarget = Targets.Any(t => t.On && t.Mode == TargetRowViewModel.ModeChoices[1]);
```

In `PushViewSettings()`, add:
```csharp
if (AnyCalloutTarget)
{
    if (!Number(CalloutScaleText, "Callout scale", 1, 5000, out double calloutScale)
        || !Number(CalloutMarginText, "Callout margin", 0, 10000, out double calloutMargin)) return false;
    _settings.CalloutScale = (int)Math.Round(calloutScale);
    _settings.CalloutMarginMm = calloutMargin;
}
_settings.CalloutViewTypeName = SelectedDetailViewType ?? string.Empty;
```

- [ ] **Step 3: Update result display in `ViewModel_WetAreaWaterproofing.Run.cs`**

In `OnRunDone`, after the `SectionsPlaced` line, add:
```csharp
if (outcome.CalloutsCreated > 0) ResultLines.Add(new ResultLine("ok", $"{outcome.CalloutsCreated} callout(s) created"));
```

Update the summary log line to include callouts.

- [ ] **Step 4: Update the XAML**

In `WetAreaWaterproofingWindow.xaml`, in the section targets DataGrid:

Add a `DataGridTemplateColumn` for Mode with a ComboBox bound to `Mode` with `ItemsSource="{x:Static vm:TargetRowViewModel.ModeChoices}"`.

Below the section targets grid (near the shared drafting view controls), add:
- A row visible when `AnyCalloutTarget` is true, containing:
  - Label: "Detail view type"
  - ComboBox bound to `SelectedDetailViewType`, ItemsSource `DetailViewTypes`
  - Label: "Callout scale"
  - TextBox bound to `CalloutScaleText`
  - Label: "Callout margin mm"
  - TextBox bound to `CalloutMarginText`

- [ ] **Step 5: Build**

Run: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Menu/Sheet/WetAreaWaterproofing_V001/UI/ViewModels/ViewModel_WetAreaWaterproofing.Views.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/UI/ViewModels/ViewModel_WetAreaWaterproofing.Run.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/UI/Views/WetAreaWaterproofingWindow.xaml
git commit -m "feat(wet-area): UI for per-target callout mode and detail view type

TargetRowViewModel gets a Mode combo (Reference section / Callout).
Detail view type dropdown, callout scale and margin appear when any
target is set to Callout. Result card shows callouts created."
```

---

### Task 4: Report, version bump, CHANGELOG, preview

**Files:**
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Core/Report/ReportWriter.cs`
- Modify: `Menu/Sheet/WetAreaWaterproofing_V001/Core/Report/RunReport.cs` (Build method)
- Modify: `Shared/Services/ToolCatalog.cs`
- Modify: `CHANGELOG.md`
- Modify or create: `Claude/Previews/WetAreaWaterproofing_V001_Window.html`

**Interfaces:**
- Consumes: `CreatedSection.IsCallout`, `.CalloutViewId`, `.CalloutViewName` (Task 2)
- Consumes: `RunOutcome.CalloutsCreated` (Task 2)

- [ ] **Step 1: Update `ReportWriter.SectionColumns`**

Append at the end:
```csharp
"Mode", "Callout view Id", "Callout view"
```

- [ ] **Step 2: Update section row writing in `ReportWriter`**

For each `CreatedSection`, write the new columns: `section.IsCallout ? "Callout" : "Reference section"`, `section.CalloutViewId` (0 → empty), `section.CalloutViewName`.

- [ ] **Step 3: Update `ReportSummary`**

Add:
```csharp
/// <summary>Callout detail views created.</summary>
public int CalloutsCreated { get; set; }
```

Wire it in `RunReport.Build` from `RunOutcome.CalloutsCreated`.

- [ ] **Step 4: Bump `ToolCatalog`**

Change:
```csharp
public static readonly ToolInfo WetAreaWaterproofing = Add("Wet Area Waterproofing", "1.2.0", "V001", ...);
```

- [ ] **Step 5: Update `CHANGELOG.md`**

Add entry under a new version heading:
```markdown
## Wet Area Waterproofing 1.2.0 — 2026-11-19

### Added
- **Per-target callout mode**: each section target can now be set to "Reference section"
  (existing behaviour, points to a drafting view) or "Callout" (creates a cropped detail
  view showing the actual model geometry). The mode is set per row in the section targets
  table.
- **Detail view type dropdown**: when any target is set to Callout, a dropdown appears to
  pick the detail view family type the callouts use. Defaults to the first detail type in
  the project.
- **Callout scale and margin settings**: control the scale and crop margin of callout views.
- Excel report's Sections sheet now shows the section mode and callout view details.
```

- [ ] **Step 6: Update `SheetToolsRibbon.cs` tooltip**

Update the tooltip string to mention callouts:
```
"…and creates a cropped plan per room with up to 6 reference sections or callouts (doors, drains, corners, " +
"fixtures) pointing to drafting views or showing model detail. Excel report; one Undo step."
```

- [ ] **Step 7: Update the HTML preview**

Update `Claude/Previews/WetAreaWaterproofing_V001_Window.html` to show the new Mode column in the section targets table mock-up and the detail view type dropdown. Mark the new elements with a green "New" tag. Publish as a claude.ai Artifact.

- [ ] **Step 8: Build and test**

Run: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true`
Run: `dotnet test Tests/WetAreaWaterproofing.Tests/ -v n`
Expected: 0 errors, all tests pass.

- [ ] **Step 9: Commit**

```bash
git add Menu/Sheet/WetAreaWaterproofing_V001/Core/Report/ReportWriter.cs \
       Menu/Sheet/WetAreaWaterproofing_V001/Core/Report/RunReport.cs \
       Shared/Services/ToolCatalog.cs CHANGELOG.md \
       Menu/00_Push_Button_Menu_items/Ribbon/SheetToolsRibbon.cs \
       Claude/Previews/WetAreaWaterproofing_V001_Window.html
git commit -m "feat(wet-area): report, version 1.2.0, changelog, preview

Sections sheet gains Mode/Callout columns. ToolCatalog bumped to 1.2.0.
CHANGELOG and HTML preview updated. Tooltip mentions callouts."
```
