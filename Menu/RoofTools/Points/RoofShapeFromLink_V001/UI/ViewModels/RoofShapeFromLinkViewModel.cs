using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Models;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Services;
using Revit26_Plugin_Revised.RoofShapeFromLink.V001.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.Shared.Models;
using Revit26_Plugin_Revised.Shared.ViewModels;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V001.UI.ViewModels
{
    /// <summary>
    /// State of the Roof Shape From Link window. It never calls the Revit API itself: every action
    /// that reads or changes the model raises the external event (see <see cref="RoofShapeFromLinkHandler"/>).
    /// </summary>
    public partial class RoofShapeFromLinkViewModel : ToolViewModelBase
    {
        private readonly RoofShapeFromLinkHandler _handler;
        private readonly ExternalEvent _event;
        private bool _loading;

        private List<ElementId> _roofIds;

        /// <summary>Footprint roofs the tool works on; replaced by "Change selection".</summary>
        public List<ElementId> RoofIds
        {
            get => _roofIds;
            set
            {
                _roofIds = value;
                RoofCount = value?.Count ?? 0;
            }
        }

        /// <summary>Number of roofs in <see cref="RoofIds"/>.</summary>
        [ObservableProperty] private int _roofCount;

        /// <summary>Project length unit, e.g. "mm".</summary>
        public string UnitLabel { get; }

        /// <summary>Converts feet to the project length unit.</summary>
        public Func<double, double> ToDisplay { get; }

        /// <summary>Converts the project length unit to feet.</summary>
        public Func<double, double> ToFeet { get; }

        /// <summary>Choices for a row's Source column.</summary>
        public IReadOnlyList<string> SourceChoices { get; } =
            new[] { ShapePointRow.SourceSurface, ShapePointRow.SourceNearest, ShapePointRow.SourceManual };

        /// <summary>Choices for the global source switch.</summary>
        public IReadOnlyList<string> GlobalSourceChoices { get; } =
            new[] { ShapePointRow.SourceSurface, ShapePointRow.SourceNearest };

        /// <summary>Loaded links in the host model.</summary>
        public ObservableCollection<LinkItem> Links { get; } = new();

        /// <summary>Links filtered by the search box.</summary>
        public ICollectionView LinksView { get; }

        /// <summary>One row per shape-editing point of the selected roofs.</summary>
        public ObservableCollection<ShapePointRow> Rows { get; } = new();

        /// <summary>Text in the link search box.</summary>
        [ObservableProperty] private string _linkSearch = "";

        /// <summary>Fixed number subtracted from every linked elevation (project length unit).</summary>
        [ObservableProperty] private double _globalOffset;

        /// <summary>Source applied to every row: Surface or Nearest.</summary>
        [ObservableProperty] private string _globalSource = ShapePointRow.SourceSurface;

        /// <summary>When on, "nearest point" only uses corner and edge points of the linked roof.</summary>
        [ObservableProperty] private bool _boundaryPointsOnly;

        /// <summary>One-line description of the roofs in use.</summary>
        [ObservableProperty] private string _roofSummary = "";

        /// <summary>Number of rows that Apply will write.</summary>
        [ObservableProperty] private int _includedCount;

        /// <summary>Number of rows with a linked elevation.</summary>
        [ObservableProperty] private int _matchedCount;

        /// <summary>Raised before the window hides for a pick; the window subscribes.</summary>
        public event Action PickStarting;

        /// <summary>Raised after a pick ends; the window subscribes.</summary>
        public event Action PickEnded;

        /// <summary>Window caption, "Roof Shape From Link — vX.Y.Z".</summary>
        public string WindowTitle => ToolCatalog.RoofShapeFromLink.Title;

        /// <summary>Creates the view model for the given roofs and project length unit.</summary>
        public RoofShapeFromLinkViewModel(List<ElementId> roofIds, ForgeTypeId lengthUnit)
        {
            RoofIds = roofIds;
            UnitLabel = LabelUtils.GetLabelForUnit(lengthUnit);
            ToDisplay = ft => UnitUtils.ConvertFromInternalUnits(ft, lengthUnit);
            ToFeet = v => UnitUtils.ConvertToInternalUnits(v, lengthUnit);

            _handler = new RoofShapeFromLinkHandler(this);
            _event = ExternalEvent.Create(_handler);

            LinksView = CollectionViewSource.GetDefaultView(Links);
            LinksView.Filter = o => o is LinkItem l &&
                (string.IsNullOrWhiteSpace(LinkSearch) || l.Name.IndexOf(LinkSearch.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ── Called by the handler (Revit thread == UI thread) ─────────────────

        /// <summary>Writes a log line.</summary>
        public void Log(LogLevel level, string message) => AddLog(level, message);

        /// <summary>Replaces the link list.</summary>
        public void SetLinks(IEnumerable<LinkItem> links)
        {
            Links.Clear();
            foreach (LinkItem l in links) Links.Add(l);
        }

        /// <summary>Replaces the grid rows and refreshes the counters.</summary>
        public void SetRows(IEnumerable<ShapePointRow> rows)
        {
            _loading = true;
            foreach (ShapePointRow old in Rows) old.PropertyChanged -= OnRowChanged;
            Rows.Clear();
            foreach (ShapePointRow r in rows)
            {
                r.PropertyChanged += OnRowChanged;
                Rows.Add(r);
            }
            _loading = false;
            UpdateCounts();
        }

        /// <summary>Marks the start or end of a long step so the status strip animates.</summary>
        public void SetBusy(bool busy, string text)
        {
            IsRunning = busy;
            if (!string.IsNullOrEmpty(text)) SummaryText = text;
        }

        /// <summary>Hides the window while the user picks roofs.</summary>
        public void BeginPick() => PickStarting?.Invoke();

        /// <summary>Shows the window again after a pick.</summary>
        public void EndPick() => PickEnded?.Invoke();

        /// <summary>Recounts matched and included rows.</summary>
        public void UpdateCounts()
        {
            MatchedCount = Rows.Count(r => r.SurfaceZ.HasValue || r.NearestZ.HasValue);
            IncludedCount = Rows.Count(r => r.Include && r.NewZ.HasValue);
        }

        // ── Reacting to the controls ──────────────────────────────────────────

        partial void OnLinkSearchChanged(string value) => LinksView.Refresh();

        partial void OnGlobalOffsetChanged(double value)
        {
            foreach (ShapePointRow r in Rows) r.Offset = value;
        }

        partial void OnGlobalSourceChanged(string value)
        {
            foreach (ShapePointRow r in Rows) r.UseSource(value);
        }

        private void OnRowChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_loading) return;
            if (e.PropertyName is nameof(ShapePointRow.Include) or nameof(ShapePointRow.NewZ))
                UpdateCounts();
        }

        // ── Commands ──────────────────────────────────────────────────────────

        /// <summary>Reads the linked elevations for the current roofs and ticked links.</summary>
        [RelayCommand]
        private void ReadElevations() => Raise(RoofShapeFromLinkHandler.Job.Compute);

        /// <summary>Lets the user pick other footprint roofs, then reads them.</summary>
        [RelayCommand]
        private void ChangeSelection() => Raise(RoofShapeFromLinkHandler.Job.PickRoofs);

        /// <summary>Writes the included rows to the roofs.</summary>
        [RelayCommand]
        private void Apply()
        {
            if (IncludedCount == 0)
            {
                AddLog(LogLevel.Warning, "Nothing to apply: tick at least one row that has a New Z.");
                return;
            }
            Raise(RoofShapeFromLinkHandler.Job.Apply);
        }

        /// <summary>Ticks every link that is visible in the search result.</summary>
        [RelayCommand]
        private void SelectAllLinks()
        {
            foreach (LinkItem l in LinksView.Cast<LinkItem>()) l.IsSelected = true;
        }

        /// <summary>Unticks every link.</summary>
        [RelayCommand]
        private void SelectNoLinks()
        {
            foreach (LinkItem l in Links) l.IsSelected = false;
        }

        /// <summary>Includes every row that has a New Z.</summary>
        [RelayCommand]
        private void IncludeAllRows()
        {
            foreach (ShapePointRow r in Rows) r.Include = r.NewZ.HasValue;
        }

        /// <summary>Excludes every row.</summary>
        [RelayCommand]
        private void IncludeNoRows()
        {
            foreach (ShapePointRow r in Rows) r.Include = false;
        }

        private void Raise(RoofShapeFromLinkHandler.Job job)
        {
            if (IsRunning) return;
            _handler.Pending = job;
            _event.Raise();
        }
    }
}
