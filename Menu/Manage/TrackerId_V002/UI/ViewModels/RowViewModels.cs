using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V002.Core.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V002.UI.ViewModels
{
    /// <summary>One row of the step 1 categories grid; ticking it adds the category to the run.</summary>
    public sealed partial class CategoryRowViewModel : ObservableObject
    {
        private readonly Action _selectionChanged;

        /// <summary>Wraps <paramref name="info"/>; <paramref name="selectionChanged"/> runs when the tick changes.</summary>
        public CategoryRowViewModel(CategoryInfo info, Action selectionChanged = null)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            _selectionChanged = selectionChanged;
        }

        /// <summary>The category as read from the model.</summary>
        public CategoryInfo Info { get; }

        /// <summary>True when the category is part of the run.</summary>
        [ObservableProperty] private bool _isSelected;

        partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke();

        /// <summary>Ticks the row without telling the owner (used when restoring several ticks at once).</summary>
        public void SetSelectedQuietly(bool value)
        {
#pragma warning disable MVVMTK0034 // Deliberately bypasses the change callback; raises PropertyChanged itself.
            if (_isSelected == value) return;
            _isSelected = value;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(IsSelected));
        }

        /// <summary>Category name.</summary>
        public string Name => Info.Name;

        /// <summary>Architecture, Structure or MEP.</summary>
        public string Discipline => Info.Discipline;

        /// <summary>Placed types.</summary>
        public int TypeCount => Info.TypeCount;

        /// <summary>Placed elements.</summary>
        public int ElementCount => Info.ElementCount;

        /// <summary>Elements that already have a Tracker_ID.</summary>
        public int WithIdCount => Info.WithIdCount;

        /// <summary>None, Partial or Complete.</summary>
        public CoverageState Coverage => CoverageOf(Info.WithIdCount, Info.ElementCount);

        /// <summary>Share of elements with an ID, 0–100, for the coverage bar.</summary>
        public double CoveragePercent => Info.ElementCount == 0 ? 0 : 100.0 * Info.WithIdCount / Info.ElementCount;

        /// <summary>"12 / 40".</summary>
        public string CoverageText => $"{Info.WithIdCount} / {Info.ElementCount}";

        /// <summary>Coverage of <paramref name="withId"/> out of <paramref name="total"/>.</summary>
        public static CoverageState CoverageOf(int withId, int total) =>
            total == 0 || withId == 0 ? CoverageState.None : withId >= total ? CoverageState.Complete : CoverageState.Partial;
    }

    /// <summary>One row of the step 2 types grid; ticking it adds the type to the run.</summary>
    public sealed partial class TypeRowViewModel : ObservableObject
    {
        private readonly Action _selectionChanged;

        /// <summary>Wraps <paramref name="info"/>; <paramref name="selectionChanged"/> runs when the tick changes.</summary>
        public TypeRowViewModel(TypeInfo info, Action selectionChanged)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            _selectionChanged = selectionChanged;
        }

        /// <summary>The type as read from the model.</summary>
        public TypeInfo Info { get; }

        /// <summary>True when the type is part of the run.</summary>
        [ObservableProperty] private bool _isSelected;

        partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke();

        /// <summary>Category name.</summary>
        public string CategoryName => Info.CategoryName;

        /// <summary>Family name.</summary>
        public string FamilyName => Info.FamilyName;

        /// <summary>Type name.</summary>
        public string TypeName => Info.TypeName;

        /// <summary>Type Mark.</summary>
        public string TypeMark => Info.TypeMark;

        /// <summary>Placed elements.</summary>
        public int ElementCount => Info.ElementCount;

        /// <summary>None, Partial or Complete.</summary>
        public CoverageState Coverage => CategoryRowViewModel.CoverageOf(Info.WithIdCount, Info.ElementCount);

        /// <summary>"3 / 8 · partial".</summary>
        public string CoverageText => $"{Info.WithIdCount} / {Info.ElementCount} · {Coverage.ToString().ToLowerInvariant()}";
    }

    /// <summary>A choice in a segment's Source drop-down.</summary>
    public sealed class SourceOption
    {
        /// <summary>Creates the option.</summary>
        public SourceOption(SegmentSource value) => Value = value;

        /// <summary>The source.</summary>
        public SegmentSource Value { get; }

        /// <summary>Display text.</summary>
        public string Label => SegmentCatalog.SourceLabel(Value);

        /// <inheritdoc/>
        public override string ToString() => Label;
    }

    /// <summary>One row of the format editor in step 3.</summary>
    public sealed partial class SegmentViewModel : ObservableObject
    {
        private readonly Action<SegmentViewModel, bool> _changed;

        /// <summary>
        /// Creates the row from <paramref name="definition"/>. <paramref name="changed"/> runs on every
        /// edit; its flag is true when a parameter name changed, so the model must be read again.
        /// </summary>
        public SegmentViewModel(SegmentDefinition definition, Action<SegmentViewModel, bool> changed)
        {
            Kind = definition.Kind;
            Sources = SegmentCatalog.SourcesFor(Kind).Select(s => new SourceOption(s)).ToList();
            _isEnabled = definition.Enabled;
            _selectedSource = Sources.FirstOrDefault(s => s.Value == definition.Source) ?? Sources[0];
            _option = definition.Option ?? string.Empty;
            _changed = changed;
        }

        /// <summary>Which segment this is.</summary>
        public SegmentKind Kind { get; }

        /// <summary>"Zone", "Custom 1"…</summary>
        public string Label => SegmentCatalog.Label(Kind);

        /// <summary>Short description under the label.</summary>
        public string Note => SegmentCatalog.Note(Kind);

        /// <summary>Sources this segment offers.</summary>
        public IReadOnlyList<SourceOption> Sources { get; }

        /// <summary>False leaves the segment out of the ID.</summary>
        [ObservableProperty] private bool _isEnabled;

        /// <summary>Where the value comes from.</summary>
        [ObservableProperty] private SourceOption _selectedSource;

        /// <summary>Parameter name, fixed text or digits, depending on the source.</summary>
        [ObservableProperty] private string _option;

        /// <summary>This segment's value for the first element, shown on the right.</summary>
        [ObservableProperty] private string _sampleValue = "—";

        /// <summary>True when the source needs the Option box.</summary>
        public bool UsesOption => SelectedSource != null && SegmentCatalog.UsesOption(SelectedSource.Value);

        /// <summary>Label in front of the Option box.</summary>
        public string OptionLabel => SelectedSource == null ? string.Empty : SegmentCatalog.OptionLabel(SelectedSource.Value);

        /// <summary>Text shown instead of the Option box when the source needs none.</summary>
        public string OptionPlaceholder => Kind switch
        {
            SegmentKind.Grid => "Centre point → grid crossings",
            SegmentKind.Model => "ARC, STR, MEP… found in the file name",
            _ => "From the model"
        };

        partial void OnIsEnabledChanged(bool value) => _changed?.Invoke(this, false);

        partial void OnSelectedSourceChanged(SourceOption value)
        {
            if (value != null && SegmentCatalog.UsesOption(value.Value))
                Option = SegmentCatalog.DefaultOption(value.Value);
            OnPropertyChanged(nameof(UsesOption));
            OnPropertyChanged(nameof(OptionLabel));
            _changed?.Invoke(this, true);
        }

        partial void OnOptionChanged(string value) =>
            _changed?.Invoke(this, SelectedSource != null && SegmentCatalog.OptionIsParameterName(SelectedSource.Value));

        /// <summary>The row as a format definition.</summary>
        public SegmentDefinition ToDefinition() => new()
        {
            Kind = Kind,
            Enabled = IsEnabled,
            Source = SelectedSource?.Value ?? Sources[0].Value,
            Option = Option ?? string.Empty
        };
    }

    /// <summary>One row of the step 3 result grid.</summary>
    public sealed class PlanRowViewModel
    {
        /// <summary>Wraps <paramref name="row"/>.</summary>
        public PlanRowViewModel(PlanRow row) => Row = row ?? throw new ArgumentNullException(nameof(row));

        /// <summary>The planner's decision.</summary>
        public PlanRow Row { get; }

        /// <summary>Revit element id.</summary>
        public long ElementId => Row.Element.ElementId;

        /// <summary>"D09 · 0900 x 2100".</summary>
        public string TypeText => string.IsNullOrEmpty(Row.Element.TypeMark)
            ? Row.Element.TypeName
            : $"{Row.Element.TypeMark} · {Row.Element.TypeName}";

        /// <summary>Zone value.</summary>
        public string Zone => Row.Zone;

        /// <summary>Level value.</summary>
        public string Level => Row.Level;

        /// <summary>"C04 · 1.2 m".</summary>
        public string GridText => Row.GridDistanceM.HasValue
            ? $"{Row.Grid} · {Row.GridDistanceM.Value.ToString("0.0", CultureInfo.InvariantCulture)} m"
            : Row.Grid;

        /// <summary>True when the grid is further than the tolerance.</summary>
        public bool GridCheck => Row.GridCheck;

        /// <summary>Tracker_ID before the run, "—" when none.</summary>
        public string ExistingId => string.IsNullOrEmpty(Row.Element.TrackerId) ? "—" : Row.Element.TrackerId;

        /// <summary>Tracker_ID after the run.</summary>
        public string ResultId => Row.ResultId;

        /// <summary>The decision.</summary>
        public PlanAction Action => Row.Action;

        /// <summary>Short label for the Action column.</summary>
        public string ActionText => Row.Action switch
        {
            PlanAction.Keep => "Keep",
            PlanAction.KeepMoved => "Keep · moved",
            PlanAction.Adopt => "Keep · stamp",
            PlanAction.Create => "Create",
            PlanAction.Copy => "Copy → new ID",
            PlanAction.Duplicate => "Duplicate",
            PlanAction.Locked => "Locked",
            _ => Row.Action.ToString()
        };

        /// <summary>Explanation for the tooltip.</summary>
        public string Note => Row.GridCheck
            ? $"{Row.Note} Nearest grid is {Row.GridDistanceM:0.0} m away; check the grid value.".Trim()
            : Row.Note;
    }

    /// <summary>One row of the step 5 status preview.</summary>
    public sealed class StatusRowViewModel
    {
        /// <summary>Wraps <paramref name="row"/>.</summary>
        public StatusRowViewModel(StatusPlanRow row) => Row = row ?? throw new ArgumentNullException(nameof(row));

        /// <summary>The merge decision.</summary>
        public StatusPlanRow Row { get; }

        /// <summary>Revit element id.</summary>
        public long ElementId => Row.Element.ElementId;

        /// <summary>Tracker_ID.</summary>
        public string TrackerId => Row.Element.TrackerId;

        /// <summary>Category name.</summary>
        public string Category => Row.Element.CategoryName;

        /// <summary>Status in Revit now, "—" when none.</summary>
        public string CurrentStatus => string.IsNullOrEmpty(Row.Element.StoredStatus) ? "—" : Row.Element.StoredStatus;

        /// <summary>Status in the file, "—" when the file has no row.</summary>
        public string FileStatus => Row.Record == null ? "—" : (string.IsNullOrEmpty(Row.Record.Status) ? "(blank)" : Row.Record.Status);

        /// <summary>Progress in the file, e.g. "75 %".</summary>
        public string FileProgress => Row.Record?.Progress == null ? string.Empty : Row.Record.Progress.Value.ToString("0.#", CultureInfo.InvariantCulture) + " %";

        /// <summary>Date in the file.</summary>
        public string FileDate => Row.Record?.DateText ?? string.Empty;

        /// <summary>The decision.</summary>
        public StatusChange Change => Row.Change;

        /// <summary>Label for the Action column.</summary>
        public string ChangeText => Row.Change switch
        {
            StatusChange.Set => "Update",
            StatusChange.Unchanged => "Up to date",
            StatusChange.NoRow => "Not in file",
            StatusChange.Locked => "Locked",
            _ => Row.Change.ToString()
        };
    }

    /// <summary>One row of the status colour table.</summary>
    public sealed partial class StatusColourRowViewModel : ObservableObject
    {
        /// <summary>Creates the row.</summary>
        public StatusColourRowViewModel(string status, string color)
        {
            _status = status ?? string.Empty;
            _color = color ?? StatusColours.NoStatusColor;
        }

        /// <summary>Status text as in the status file.</summary>
        [ObservableProperty] private string _status;

        /// <summary>Colour as "#RRGGBB"; the swatch binds to it.</summary>
        [ObservableProperty] private string _color;

        /// <summary>The row as a settings entry.</summary>
        public StatusColour ToModel() => new() { Status = Status?.Trim() ?? string.Empty, Color = Color?.Trim() ?? string.Empty };
    }

    /// <summary>One row of the step 6 health check.</summary>
    public sealed class HealthRowViewModel
    {
        /// <summary>Wraps <paramref name="issue"/>.</summary>
        public HealthRowViewModel(HealthIssue issue) => Issue = issue ?? throw new ArgumentNullException(nameof(issue));

        /// <summary>The finding.</summary>
        public HealthIssue Issue { get; }

        /// <summary>What was found.</summary>
        public HealthIssueKind Kind => Issue.Kind;

        /// <summary>Label for the Problem column.</summary>
        public string KindText => KindLabel(Issue.Kind);

        /// <summary>Element id, "—" for an ID only in the status file.</summary>
        public string ElementIdText => Issue.ElementId?.ToString(CultureInfo.InvariantCulture) ?? "—";

        /// <summary>Tracker_ID, "—" when none.</summary>
        public string TrackerId => string.IsNullOrEmpty(Issue.TrackerId) ? "—" : Issue.TrackerId;

        /// <summary>Category name.</summary>
        public string Category => Issue.Category;

        /// <summary>Type.</summary>
        public string Type => Issue.Type;

        /// <summary>Explanation.</summary>
        public string Detail => Issue.Detail;

        /// <summary>Display label of a kind.</summary>
        public static string KindLabel(HealthIssueKind kind) => kind switch
        {
            HealthIssueKind.DuplicateId => "Duplicate ID",
            HealthIssueKind.Copy => "Copy",
            HealthIssueKind.MissingId => "Missing ID",
            HealthIssueKind.Moved => "Moved",
            HealthIssueKind.FarFromGrid => "Far from grid",
            HealthIssueKind.NoStatus => "No status",
            HealthIssueKind.IdNotInModel => "ID not in model",
            _ => kind.ToString()
        };
    }
}
