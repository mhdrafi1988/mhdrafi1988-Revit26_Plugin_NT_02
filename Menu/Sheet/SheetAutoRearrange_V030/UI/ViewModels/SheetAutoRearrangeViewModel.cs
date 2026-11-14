using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Services;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Infrastructure.ExternalEvents;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Infrastructure.Helpers;
using Revit26_Plugin_Revised.Shared.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace Revit26_Plugin_Revised.SheetAutoRearrange.V030.UI.ViewModels
{
    /// <summary>
    /// V011 CHANGES (all per explicit request):
    ///  - Settings persistence: loads from and saves to
    ///    %AppData%\Revit26_Plugin_Revised\SheetAutoRearrange\settings.json on
    ///    construct / window close / after each Run, per suite convention.
    ///    Previously this tool had NO persistence at all — every setting
    ///    reset to its hardcoded default on every open.
    ///  - New Gap & Margin defaults (Top 30 / Bottom 150 / Left 30 /
    ///    Right 200mm) — set in GapSettings.cs, only takes effect on a
    ///    FIRST run (no existing settings.json yet); once persistence is in
    ///    place, a person's saved values always win over hardcoded defaults.
    ///  - New Sheet Position defaults: H = Left, V = Top (overrides the
    ///    V010 Right/Bottom defaults) — same first-run-only caveat.
    ///  - Layout Settings / Sheet Positioning split into two separate
    ///    expanders (previously one always-visible card) — see XAML.
    /// </summary>
    public partial class SheetAutoRearrangeViewModel : ObservableObject, System.IDisposable
    {
        private const string ToolName = "SheetAutoRearrange";

        private readonly UIDocument _uiDoc;
        private readonly SheetAutoRearrangeEventHandler _handler;
        private readonly ExternalEvent _externalEvent;
        private readonly Dispatcher _dispatcher;

        [ObservableProperty] private string lastLogFilePath = string.Empty;

        [ObservableProperty] private string sheetChipText = "No active sheet";
        [ObservableProperty] private bool isSheetActive;
        [ObservableProperty] private bool isBusy;

        // ToolWindowShell status strip (CLAUDE.md v1.1)
        [ObservableProperty] private double progress;
        [ObservableProperty] private string summaryText = "";

        [ObservableProperty] private ObservableCollection<ViewOnSheetItem> views = new();
        [ObservableProperty] private ObservableCollection<LogEntry> log = new();

        [ObservableProperty] private string filterText = string.Empty;

        // ── View Type filter popover state (not persisted — resets per session) ──
        [ObservableProperty] private bool showFloorPlan = true;
        [ObservableProperty] private bool showSection = true;
        [ObservableProperty] private bool showElevation = true;
        [ObservableProperty] private bool show3D = true;
        [ObservableProperty] private bool showLegend;
        [ObservableProperty] private bool showSchedule;
        [ObservableProperty] private bool showDrafting;

        // ── Layout Settings (Row Tolerance, Within-Row Align, Shelf
        // Sub-Row, + V012 NEW: Column Tolerance, Within-Column Align, Shelf
        // Sub-Col Align — moved here from Sheet Positioning). Defaults
        // below are FALLBACKS only — LoadPersistedSettings() overwrites
        // them with saved values if settings.json exists. ──
        [ObservableProperty] private double rowToleranceMm = 50;
        [ObservableProperty] private RowAlignment rowAlignment = RowAlignment.Bottom;

        /// <summary>Only affects placement when LeftoverGroupMode is Column — see that property.</summary>
        [ObservableProperty] private double columnToleranceMm = 50;

        /// <summary>Only affects placement when LeftoverGroupMode is Column — see LeftoverGroupMode.</summary>
        [ObservableProperty] private BlockAlignmentH withinColumnAlign = BlockAlignmentH.Right;

        /// <summary>
        /// V024 NEW: selects whether the leftover-item fallback in
        /// SheetOrderPackingService (items the main fill strategy above
        /// couldn't place) groups by row (RowToleranceMm/RowAlignment) or by
        /// column (ColumnToleranceMm/WithinColumnAlign). Defaults to Row —
        /// the only behavior that existed before this was wired in.
        /// </summary>
        [ObservableProperty] private LeftoverGroupMode leftoverGroupMode = LeftoverGroupMode.Row;

        // ── V026 NEW: Priority Groups — lets the user rank ViewTypes (e.g.
        // Sections before Drafting Views) so every view of the 1st-ranked
        // type places before any view of the 2nd-ranked type, and so on.
        // PriorityGroups is rebuilt (RebuildPriorityGroups) every time
        // Views reloads, from the ViewTypes actually present on the sheet —
        // see that method for how a saved rank merges with newly-seen
        // types. Off by default — existing behavior (one pool) is
        // unchanged until a person explicitly turns this on. ──
        [ObservableProperty] private bool enablePriorityGroups;
        [ObservableProperty] private ObservableCollection<PriorityGroupRow> priorityGroups = new();

        /// <summary>Rank order loaded from settings.json, held until the first Views load so RebuildPriorityGroups can merge it against whatever ViewTypes are actually present. Null once consumed.</summary>
        private List<ViewType>? _pendingSavedPriorityOrder;

        // ── Sheet Positioning (Sheet Position H/V only — Shelf Sub-Col
        // Align moved OUT to Layout Settings per explicit request) ──
        /// <summary>V011: default changed to Left (was Right in V010) per explicit request.</summary>
        [ObservableProperty] private BlockAlignmentH columnAlignH = BlockAlignmentH.Left;

        /// <summary>V011: default changed to Top (was Bottom in V010) per explicit request.</summary>
        [ObservableProperty] private BlockAlignmentV columnAlignV = BlockAlignmentV.Top;

        // ── Overflow Handling ─────────────────────────────────────────────
        [ObservableProperty] private OverflowHandlingMode overflowHandlingMode = OverflowHandlingMode.PlaceWhatsPlaceable;

        // ── Gap & Margin Settings ─────────────────────────────────────────
        [ObservableProperty] private GapSettings gapSettings = new();

        // ── V014: Row Fill Strategy (replaces Tall/Wide View Detection) ───
        // V022 CHANGE: default changed Shelf -> MaxFill per explicit spec
        // ("new default"). Only affects fresh installs with no saved
        // settings.json — LoadSettings below overwrites this with
        // saved.RowFillStrategy for any existing user.
        [ObservableProperty] private RowFillStrategy rowFillStrategy = RowFillStrategy.MaxFill;

        // ── V029 NEW: Align & Distribute. All Off by default = the V028 layout. ──
        /// <summary>Row line the views of one row share (Off / Top / Center / Bottom).</summary>
        [ObservableProperty] private GridRowAlign alignRows = GridRowAlign.Off;

        /// <summary>Column line the views of one column share (Off / Left / Center / Right).</summary>
        [ObservableProperty] private GridColumnAlign alignColumns = GridColumnAlign.Off;

        /// <summary>How rows / columns spread across the usable width.</summary>
        [ObservableProperty] private DistributeMode distributeH = DistributeMode.Off;

        /// <summary>How rows / columns spread across the usable height.</summary>
        [ObservableProperty] private DistributeMode distributeV = DistributeMode.Off;

        /// <summary>Latest Align &amp; Distribute result from the live preview (applied / skipped). Empty when off.</summary>
        [ObservableProperty] private string alignNote = string.Empty;

        /// <summary>True when <see cref="AlignNote"/> reports that alignment was skipped.</summary>
        [ObservableProperty] private bool isAlignNoteWarning;

        /// <summary>Distribute needs rows or columns to spread, so it is only available with an alignment on.</summary>
        public bool IsDistributeEnabled => AlignRows != GridRowAlign.Off || AlignColumns != GridColumnAlign.Off;

        /// <summary>Header badge of the Align &amp; Distribute expander: Off, Rows, Columns or Table.</summary>
        public string AlignSummary =>
            AlignRows != GridRowAlign.Off && AlignColumns != GridColumnAlign.Off ? "Table"
            : AlignRows != GridRowAlign.Off ? "Rows"
            : AlignColumns != GridColumnAlign.Off ? "Columns"
            : "Off";

        partial void OnAlignRowsChanged(GridRowAlign value) => RaiseAlignStateChanged();
        partial void OnAlignColumnsChanged(GridColumnAlign value) => RaiseAlignStateChanged();

        private void RaiseAlignStateChanged()
        {
            OnPropertyChanged(nameof(IsDistributeEnabled));
            OnPropertyChanged(nameof(AlignSummary));
            OnPropertyChanged(nameof(ActiveQuickLayout));
            OnPropertyChanged(nameof(IsQuickPack));
            OnPropertyChanged(nameof(IsQuickRows));
            OnPropertyChanged(nameof(IsQuickTable));
            OnPropertyChanged(nameof(IsQuickSpread));
        }

        /// <summary>The four Align &amp; Distribute options as one settings object (for packing and saving).</summary>
        private AlignDistributeSettings BuildAlignDistribute() => new()
        {
            AlignRows = AlignRows,
            AlignColumns = AlignColumns,
            DistributeH = DistributeH,
            DistributeV = DistributeV
        };

        // ── V030 NEW: redesigned window state ─────────────────────────────

        /// <summary>One entry of the Fill strategy dropdown: the value, its name and a one-line description.</summary>
        public sealed class StrategyOption
        {
            public RowFillStrategy Value { get; }
            public string Name { get; }
            public string Description { get; }

            public StrategyOption(RowFillStrategy value, string name, string description)
            {
                Value = value;
                Name = name;
                Description = description;
            }
        }

        /// <summary>The seven fill strategies, in the order the dropdown lists them.</summary>
        public IReadOnlyList<StrategyOption> StrategyOptions { get; } = new List<StrategyOption>
        {
            new(RowFillStrategy.MaxFill, "Max Fill (recommended)", "Densest. Smallest views first, into the free space."),
            new(RowFillStrategy.RafisAlgo, "Rafi's Algo", "Rows by tallest view, columns stacked inside each row."),
            new(RowFillStrategy.Skyline, "Skyline", "Fast. Each view drops to the lowest free spot."),
            new(RowFillStrategy.SkylineWasteMap, "Skyline + Waste", "Skyline that also reuses the pockets it leaves."),
            new(RowFillStrategy.Shelf, "Shelf", "Simple rows grouped by height."),
            new(RowFillStrategy.Guillotine, "Guillotine", "Rows by height, balanced 2-way split."),
            new(RowFillStrategy.MaxRects, "MaxRects", "Rows by height, densest 4-way split."),
        };

        /// <summary>Which Quick layout the align / distribute options currently match: Pack, Rows, Table, Spread, or empty for a custom mix.</summary>
        public string ActiveQuickLayout
        {
            get
            {
                bool rows = AlignRows != GridRowAlign.Off, cols = AlignColumns != GridColumnAlign.Off;
                bool dh = DistributeH != DistributeMode.Off, dv = DistributeV != DistributeMode.Off;
                if (!rows && !cols) return "Pack";
                if (rows && !cols && !dh && !dv) return "Rows";
                if (rows && cols && !dh && !dv) return "Table";
                if (rows && cols && dh && dv) return "Spread";
                return string.Empty;
            }
        }

        public bool IsQuickPack => ActiveQuickLayout == "Pack";
        public bool IsQuickRows => ActiveQuickLayout == "Rows";
        public bool IsQuickTable => ActiveQuickLayout == "Table";
        public bool IsQuickSpread => ActiveQuickLayout == "Spread";

        /// <summary>Sets the four align / distribute options in one click: Pack, Rows, Table or Spread.</summary>
        [RelayCommand]
        private void ApplyQuickLayout(string key)
        {
            switch (key)
            {
                case "Pack":
                    (AlignRows, AlignColumns, DistributeH, DistributeV) = (GridRowAlign.Off, GridColumnAlign.Off, DistributeMode.Off, DistributeMode.Off);
                    break;
                case "Rows":
                    (AlignRows, AlignColumns, DistributeH, DistributeV) = (GridRowAlign.Bottom, GridColumnAlign.Off, DistributeMode.Off, DistributeMode.Off);
                    break;
                case "Table":
                    (AlignRows, AlignColumns, DistributeH, DistributeV) = (GridRowAlign.Bottom, GridColumnAlign.Left, DistributeMode.Off, DistributeMode.Off);
                    break;
                case "Spread":
                    (AlignRows, AlignColumns, DistributeH, DistributeV) = (GridRowAlign.Bottom, GridColumnAlign.Left, DistributeMode.EdgeToEdge, DistributeMode.EqualSpacing);
                    break;
            }
        }

        /// <summary>Sheet position as one "Vertical,Horizontal" value (e.g. "Top,Left") for the 3 x 3 picker.</summary>
        public string SheetPosition
        {
            get => $"{ColumnAlignV},{ColumnAlignH}";
            set
            {
                var parts = (value ?? string.Empty).Split(',');
                if (parts.Length == 2
                    && System.Enum.TryParse<BlockAlignmentV>(parts[0], out var v)
                    && System.Enum.TryParse<BlockAlignmentH>(parts[1], out var h))
                {
                    ColumnAlignV = v;
                    ColumnAlignH = h;
                }
            }
        }

        partial void OnColumnAlignHChanged(BlockAlignmentH value) => OnPropertyChanged(nameof(SheetPosition));
        partial void OnColumnAlignVChanged(BlockAlignmentV value) => OnPropertyChanged(nameof(SheetPosition));
        partial void OnDistributeHChanged(DistributeMode value) => RaiseAlignStateChanged();
        partial void OnDistributeVChanged(DistributeMode value) => RaiseAlignStateChanged();

        /// <summary>Views that fit the usable area in the current preview.</summary>
        [ObservableProperty] private int previewFitCount;

        /// <summary>Views that do not fit and would go below the sheet.</summary>
        [ObservableProperty] private int previewOverflowCount;

        /// <summary>Unticked views (of the shown types) that Run removes from the sheet.</summary>
        [ObservableProperty] private int toRemoveCount;

        /// <summary>Activity log open / collapsed. Opens by itself on a warning or error.</summary>
        [ObservableProperty] private bool isLogExpanded;

        /// <summary>Latest log message, shown on the collapsed log bar.</summary>
        [ObservableProperty] private string lastLogText = string.Empty;

        private void OnLogChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems == null || e.NewItems.Count == 0)
            {
                if (Log.Count == 0)
                    LastLogText = string.Empty;
                return;
            }

            foreach (LogEntry entry in e.NewItems)
            {
                LastLogText = entry.Message;
                if (entry.Level == LogLevel.Warning || entry.Level == LogLevel.Error)
                    IsLogExpanded = true;
            }
        }

        // Title block box behind _region: lets the usable area follow the margin boxes as they are typed.
        private RectFeet? _titleBlockRect;
        private (double Top, double Bottom, double Left, double Right) _loadMargins;

        /// <summary>The title block's own box (the sheet outline in the preview), or null when none was detected.</summary>
        public RectFeet? TitleBlockRect => _titleBlockRect;

        /// <summary>The usable area for the margins typed right now: the title block box inset by them. Null when it has no size.</summary>
        private PlaceableRegion? CurrentRegion()
        {
            if (_region == null || _titleBlockRect == null)
                return _region;

            const double mm = 1.0 / 304.8;
            var tb = _titleBlockRect.Value;
            var min = new XYZ(tb.Min.X + GapSettings.MarginLeftMm * mm, tb.Min.Y + GapSettings.MarginBottomMm * mm, 0);
            var max = new XYZ(tb.Max.X - GapSettings.MarginRightMm * mm, tb.Max.Y - GapSettings.MarginTopMm * mm, 0);
            // Margins larger than the title block: same fallback as TitleBlockDetectionService (ignore them).
            if (max.X - min.X <= 1e-6 || max.Y - min.Y <= 1e-6)
                return new PlaceableRegion(_region.Mode, tb, "Detected (margins too large — ignored)");
            return new PlaceableRegion(_region.Mode, new RectFeet(min, max), _region.DisplayText);
        }

        [ObservableProperty] private bool isMultipleTitleBlocksWarning;

        // ── Expander state (NOT persisted — resets to these defaults on every
        // open; only the underlying VALUES persist). Row Fill Strategy and
        // Live Sheet Preview default to open per explicit request; the rest
        // stay collapsed as originally confirmed. ──
        [ObservableProperty] private bool isLayoutSettingsExpanded;
        [ObservableProperty] private bool isSheetPositioningExpanded;
        [ObservableProperty] private bool isRowFillStrategyExpanded = true;
        [ObservableProperty] private bool isLiveSheetPreviewExpanded = true;
        [ObservableProperty] private bool isOverflowHandlingExpanded;
        [ObservableProperty] private bool isGapMarginExpanded;
        [ObservableProperty] private bool isPriorityGroupsExpanded;
        [ObservableProperty] private bool isAlignDistributeExpanded = true;

        // ── Metrics ───────────────────────────────────────────────────────
        [ObservableProperty] private int totalViewsMetric;
        [ObservableProperty] private int selectedMetric;
        [ObservableProperty] private int removedMetric;
        [ObservableProperty] private int placedOkMetric;
        [ObservableProperty] private int failedToFitMetric;

        private ViewSheet? _activeSheet;
        private PlaceableRegion? _region;

        private readonly SheetOrderPackingService _sheetOrderPreview = new();

        public SheetAutoRearrangeViewModel(UIDocument uiDoc)
        {
            _uiDoc = uiDoc;
            _dispatcher = Dispatcher.CurrentDispatcher;

            _handler = new SheetAutoRearrangeEventHandler();
            _handler.Completed += OnHandlerCompleted;

            _externalEvent = ExternalEvent.Create(_handler);

            Log.CollectionChanged += OnLogChanged;

            LoadPersistedSettings();
            LoadActiveSheet();

            // Live sync: reload the grid automatically whenever the active
            // sheet's viewports change from elsewhere in Revit (add/delete/
            // move a view on the sheet, undo, etc.) — no manual Refresh
            // needed. Guarded by IsBusy so our own Load/Run actions don't
            // re-trigger themselves.
            _uiDoc.Document.Application.DocumentChanged += OnDocumentChanged;
        }

        /// <summary>Unhooks the DocumentChanged subscription. Called by the window's Closing handler alongside SaveSettings.</summary>
        public void Detach()
        {
            // FIX: Revit only lets DocumentChanged be unsubscribed inside an API context, and a
            // window's Closing event is not one ("Revit is currently not within an API context").
            // So the handler does it on its next Execute; until then _closed makes the event a no-op.
            _closed = true;
            _handler.DetachCallback = app => app.Application.DocumentChanged -= OnDocumentChanged;
            _handler.DetachOnly = !IsBusy;
            _handler.DetachRequested = true;
            _externalEvent.Raise();
        }

        private bool _closed;

        /// <summary>Releases the handler subscription and the external event, once nothing is pending on them.</summary>
        private void Cleanup()
        {
            _handler.Completed -= OnHandlerCompleted;
            _externalEvent?.Dispose();
        }

        /// <summary>
        /// V025 FIX: this ViewModel owns _handler and subscribed to its
        /// Completed event, but never unsubscribed or disposed the
        /// ExternalEvent — the same leak shape already fixed in
        /// RefSectionHeadPlacer_V013 (see that ViewModel's own MEMORY
        /// comment). Without this, _handler stays subscribed to this VM
        /// instance (and Revit's ExternalEvent registry keeps _handler
        /// alive) after the window closes, keeping the whole VM — and
        /// every Views/Log collection it owns — alive for the rest of the
        /// Revit session. Called from the window's Closing handler
        /// alongside SaveSettings/Detach.
        /// </summary>
        public void Dispose()
        {
            // A pending Detach still needs the external event; OnHandlerCompleted cleans up after it.
            if (_handler.DetachRequested)
                return;
            Cleanup();
        }

        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (_closed || IsBusy || _activeSheet == null || !ReferenceEquals(e.GetDocument(), _uiDoc.Document))
                return;

            var changedIds = e.GetAddedElementIds()
                .Concat(e.GetDeletedElementIds())
                .Concat(e.GetModifiedElementIds());

            bool relevant = false;
            foreach (var id in changedIds)
            {
                if (id == _activeSheet.Id || _uiDoc.Document.GetElement(id) is Viewport)
                {
                    relevant = true;
                    break;
                }
            }

            if (!relevant)
                return;

            Log.Add(new LogEntry(LogLevel.Info, "Sheet changed in Revit — auto-refreshing…"));
            RaiseLoad();
        }

        /// <summary>
        /// V011 NEW: loads settings.json if it exists and applies every
        /// value onto this ViewModel's properties, overriding the
        /// [ObservableProperty] hardcoded defaults declared above. If no
        /// file exists yet (first run), SettingsService.Load returns a
        /// fresh SheetAutoRearrangeSettings with ITS OWN defaults — which
        /// mirror the same values already declared here, so behavior is
        /// identical either way on a true first run.
        /// </summary>
        private void LoadPersistedSettings()
        {
            var saved = SettingsService<SheetAutoRearrangeSettings>.Load(ToolName);

            GapSettings = saved.GapSettings;
            RowFillStrategy = saved.RowFillStrategy;
            RowToleranceMm = saved.RowToleranceMm;
            RowAlignment = saved.RowAlignment;
            ColumnToleranceMm = saved.ColumnToleranceMm;
            WithinColumnAlign = saved.WithinColumnAlign;
            LeftoverGroupMode = saved.LeftoverGroupMode;
            ColumnAlignH = saved.ColumnAlignH;
            ColumnAlignV = saved.ColumnAlignV;
            OverflowHandlingMode = saved.OverflowHandlingMode;

            var align = saved.AlignDistribute ?? new AlignDistributeSettings();
            AlignRows = align.AlignRows;
            AlignColumns = align.AlignColumns;
            DistributeH = align.DistributeH;
            DistributeV = align.DistributeV;

            EnablePriorityGroups = saved.EnablePriorityGroups;
            // Views hasn't loaded yet at this point (LoadActiveSheet/RaiseLoad
            // runs right after this method returns) — RebuildPriorityGroups
            // needs the actual ViewTypes on the sheet before it can turn this
            // saved rank into PriorityGroups rows, so it's stashed here and
            // consumed the first time OnHandlerCompleted's Load case runs.
            _pendingSavedPriorityOrder = saved.PriorityOrder;
        }

        /// <summary>
        /// V011 NEW: writes the current state of every persisted setting to
        /// settings.json. Called after each successful Run (so changes made
        /// mid-session survive even if the window crashes before close) and
        /// again on window Closing (code-behind calls this directly).
        /// </summary>
        public void SaveSettings()
        {
            var toSave = new SheetAutoRearrangeSettings
            {
                GapSettings = GapSettings,
                RowFillStrategy = RowFillStrategy,
                RowToleranceMm = RowToleranceMm,
                RowAlignment = RowAlignment,
                ColumnToleranceMm = ColumnToleranceMm,
                WithinColumnAlign = WithinColumnAlign,
                LeftoverGroupMode = LeftoverGroupMode,
                ColumnAlignH = ColumnAlignH,
                ColumnAlignV = ColumnAlignV,
                OverflowHandlingMode = OverflowHandlingMode,
                EnablePriorityGroups = EnablePriorityGroups,
                PriorityOrder = PriorityGroups.Select(g => g.ViewType).ToList(),
                AlignDistribute = BuildAlignDistribute()
            };

            SettingsService<SheetAutoRearrangeSettings>.Save(ToolName, toSave);
        }

        private void LoadActiveSheet()
        {
            _activeSheet = _uiDoc.ActiveView as ViewSheet;

            if (_activeSheet == null)
            {
                IsSheetActive = false;
                SheetChipText = "Active view is not a sheet";
                Log.Add(new LogEntry(LogLevel.Warning, "Active view is not a sheet — open a sheet and reopen this tool."));
                return;
            }

            IsSheetActive = true;
            SheetChipText = $"Active Sheet: {_activeSheet.SheetNumber} — {_activeSheet.Name}";
            Log.Add(new LogEntry(LogLevel.Info, $"Active sheet detected: {_activeSheet.SheetNumber} — {_activeSheet.Name}."));

            RaiseLoad();
        }

        [RelayCommand]
        private void Refresh()
        {
            if (_activeSheet == null)
                return;

            Log.Add(new LogEntry(LogLevel.Info, "Refreshing views on sheet…"));
            RaiseLoad();
        }

        private void RaiseLoad()
        {
            IsBusy = true;
            _handler.Action = SheetAutoRearrangeAction.LoadViewsOnSheet;
            _handler.TargetSheet = _activeSheet;
            _handler.GapSettings = GapSettings;
            _loadMargins = (GapSettings.MarginTopMm, GapSettings.MarginBottomMm, GapSettings.MarginLeftMm, GapSettings.MarginRightMm);
            _externalEvent.Raise();
        }

        public System.Collections.Generic.List<PackedViewPlacement> PreviewPack()
        {
            var shown = Views.Where(v => TypeIsVisible(v.ViewType)).ToList();
            var ticked = shown.Where(v => v.IsChecked).ToList();
            var region = CurrentRegion();

            ToRemoveCount = shown.Count - ticked.Count;
            foreach (var v in shown.Where(v => !v.IsChecked))
                v.FitStatus = ViewFitStatus.NotSelected;

            if (ticked.Count == 0 || region == null)
            {
                AlignNote = string.Empty;
                IsAlignNoteWarning = false;
                PreviewFitCount = 0;
                PreviewOverflowCount = 0;
                UpdateSummary();
                return new System.Collections.Generic.List<PackedViewPlacement>();
            }

            var notes = new List<PackNote>();
            var placements = _sheetOrderPreview.Pack(ticked, region, GapSettings,
                RowToleranceMm, RowAlignment, ColumnAlignH, ColumnAlignV,
                RowFillStrategy, LeftoverGroupMode, ColumnToleranceMm, WithinColumnAlign,
                EnablePriorityGroups, PriorityGroups.Select(g => g.ViewType).ToList(),
                BuildAlignDistribute(), notes);

            // Under the preview: whether it could be aligned, and how many views fit.
            var note = notes.LastOrDefault();
            AlignNote = note?.Message ?? string.Empty;
            IsAlignNoteWarning = note?.IsWarning ?? false;

            foreach (var p in placements)
                p.Item.FitStatus = p.Fits ? ViewFitStatus.Fits : ViewFitStatus.Overflow;
            PreviewFitCount = placements.Count(p => p.Fits);
            PreviewOverflowCount = placements.Count - PreviewFitCount;
            UpdateSummary();

            return placements;
        }

        /// <summary>Footer line: what Run will do with the current ticks.</summary>
        private void UpdateSummary()
        {
            if (IsBusy)
                return;
            int move = PreviewFitCount + PreviewOverflowCount;
            SummaryText = _activeSheet == null ? "Open a sheet, then reopen this tool."
                : _lastRunText.Length > 0 ? _lastRunText
                : $"{move} view(s) will move, {ToRemoveCount} will be removed";
        }

        // Result of the last Run, kept on the footer line until an option or a tick changes.
        private string _lastRunText = string.Empty;

        private static readonly HashSet<string> OptionProperties = new()
        {
            nameof(AlignRows), nameof(AlignColumns), nameof(DistributeH), nameof(DistributeV), nameof(RowFillStrategy),
            nameof(ColumnAlignH), nameof(ColumnAlignV), nameof(RowToleranceMm), nameof(ColumnToleranceMm),
            nameof(RowAlignment), nameof(WithinColumnAlign), nameof(LeftoverGroupMode), nameof(EnablePriorityGroups)
        };

        /// <inheritdoc/>
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && OptionProperties.Contains(e.PropertyName))
                _lastRunText = string.Empty;
            base.OnPropertyChanged(e);
        }

        /// <summary>The usable area the preview draws: the title block box inset by the margins typed right now.</summary>
        public PlaceableRegion? Region => CurrentRegion();


        /// <summary>
        /// V026 NEW: rebuilds PriorityGroups from the ViewTypes actually
        /// present in the just-loaded Views. Preserves the existing rank for
        /// any type that's still present (first from the saved
        /// settings.json order on the very first load, via
        /// _pendingSavedPriorityOrder; after that, from PriorityGroups'
        /// current order, so a mid-session re-Refresh doesn't reshuffle
        /// ranks the user already set); any newly-seen type is appended at
        /// the end, in its first-appearance order. Types no longer present
        /// simply drop out of the visible list — they aren't lost from
        /// settings.json unless the user saves again without them.
        /// </summary>
        private void RebuildPriorityGroups()
        {
            var presentTypes = Views.Select(v => v.ViewType).Distinct().ToList();

            var priorSource = _pendingSavedPriorityOrder ?? PriorityGroups.Select(g => g.ViewType).ToList();
            var ordered = priorSource.Where(t => presentTypes.Contains(t)).ToList();

            foreach (var t in presentTypes)
                if (!ordered.Contains(t))
                    ordered.Add(t);

            PriorityGroups = new ObservableCollection<PriorityGroupRow>(
                ordered.Select(t => new PriorityGroupRow(t, FriendlyViewTypeName(t))));

            _pendingSavedPriorityOrder = null;
        }

        private static string FriendlyViewTypeName(ViewType viewType) => viewType switch
        {
            ViewType.FloorPlan => "Floor Plans",
            ViewType.CeilingPlan => "Ceiling Plans",
            ViewType.AreaPlan => "Area Plans",
            ViewType.EngineeringPlan => "Engineering Plans",
            ViewType.Section => "Sections",
            ViewType.Elevation => "Elevations",
            ViewType.ThreeD => "3D Views",
            ViewType.Detail => "Detail Views",
            ViewType.DraftingView => "Drafting Views",
            ViewType.Legend => "Legends",
            ViewType.Schedule => "Schedules",
            ViewType.PanelSchedule => "Panel Schedules",
            ViewType.Walkthrough => "Walkthroughs",
            ViewType.Rendering => "Renderings",
            _ => viewType.ToString()
        };

        [RelayCommand]
        private void MovePriorityGroupUp(PriorityGroupRow row)
        {
            if (row == null)
                return;
            int idx = PriorityGroups.IndexOf(row);
            if (idx > 0)
                PriorityGroups.Move(idx, idx - 1);
        }

        [RelayCommand]
        private void MovePriorityGroupDown(PriorityGroupRow row)
        {
            if (row == null)
                return;
            int idx = PriorityGroups.IndexOf(row);
            if (idx >= 0 && idx < PriorityGroups.Count - 1)
                PriorityGroups.Move(idx, idx + 1);
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (var v in FilteredViewsSource.Cast<ViewOnSheetItem>().ToList())
                v.IsChecked = true;
            UpdateMetrics();
        }

        [RelayCommand]
        private void ClearAll()
        {
            foreach (var v in FilteredViewsSource.Cast<ViewOnSheetItem>().ToList())
                v.IsChecked = false;
            UpdateMetrics();
        }

        /// <summary>
        /// Live, filtered view of Views for the DataGrid to bind to.
        ///
        /// Replaces the earlier plain-IEnumerable "FilteredViews" computed
        /// property, which required an explicit PropertyChanged(nameof(...))
        /// call from every single property it read — miss one and the grid
        /// silently goes stale/empty. An ICollectionView over the live
        /// ObservableCollection is the standard WPF pattern instead: row
        /// add/remove propagates automatically from the underlying
        /// collection, and Filter/Refresh() is the one, explicit way to
        /// re-apply the current filter — no per-property notification
        /// bookkeeping to get wrong.
        /// </summary>
        public ICollectionView FilteredViewsSource { get; private set; } = CollectionViewSource.GetDefaultView(new ObservableCollection<ViewOnSheetItem>());

        private bool FilterPredicate(object obj) =>
            obj is ViewOnSheetItem v &&
            (string.IsNullOrWhiteSpace(FilterText) || v.ViewName.Contains(FilterText, System.StringComparison.OrdinalIgnoreCase)) &&
            TypeIsVisible(v.ViewType);

        // Also re-wires the per-row IsChecked subscription whenever the Views
        // collection itself is replaced (load/refresh/run), so the SELECTED
        // metric stays live no matter which row's checkbox changes it — not
        // just the ones that go through SelectAll/ClearAll.
        partial void OnViewsChanged(ObservableCollection<ViewOnSheetItem> oldValue, ObservableCollection<ViewOnSheetItem> newValue)
        {
            if (oldValue != null)
                foreach (var item in oldValue)
                    item.PropertyChanged -= OnViewItemPropertyChanged;

            if (newValue != null)
                foreach (var item in newValue)
                    item.PropertyChanged += OnViewItemPropertyChanged;

            FilteredViewsSource = CollectionViewSource.GetDefaultView(newValue ?? new ObservableCollection<ViewOnSheetItem>());
            FilteredViewsSource.Filter = FilterPredicate;
            OnPropertyChanged(nameof(FilteredViewsSource));
        }

        private void OnViewItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewOnSheetItem.IsChecked))
            {
                _lastRunText = string.Empty;
                UpdateMetrics();
            }
        }

        partial void OnFilterTextChanged(string value) => FilteredViewsSource.Refresh();
        partial void OnShowFloorPlanChanged(bool value) => FilteredViewsSource.Refresh();
        partial void OnShowSectionChanged(bool value) => FilteredViewsSource.Refresh();
        partial void OnShowElevationChanged(bool value) => FilteredViewsSource.Refresh();
        partial void OnShow3DChanged(bool value) => FilteredViewsSource.Refresh();
        partial void OnShowLegendChanged(bool value) => FilteredViewsSource.Refresh();
        partial void OnShowScheduleChanged(bool value) => FilteredViewsSource.Refresh();
        partial void OnShowDraftingChanged(bool value) => FilteredViewsSource.Refresh();

        private bool TypeIsVisible(Autodesk.Revit.DB.ViewType viewType)
        {
            return viewType switch
            {
                Autodesk.Revit.DB.ViewType.FloorPlan or Autodesk.Revit.DB.ViewType.CeilingPlan or Autodesk.Revit.DB.ViewType.AreaPlan => ShowFloorPlan,
                Autodesk.Revit.DB.ViewType.Section => ShowSection,
                Autodesk.Revit.DB.ViewType.Elevation => ShowElevation,
                Autodesk.Revit.DB.ViewType.ThreeD => Show3D,
                Autodesk.Revit.DB.ViewType.Legend => ShowLegend,
                Autodesk.Revit.DB.ViewType.Schedule or Autodesk.Revit.DB.ViewType.PanelSchedule => ShowSchedule,
                Autodesk.Revit.DB.ViewType.DraftingView or Autodesk.Revit.DB.ViewType.Detail => ShowDrafting,
                _ => true
            };
        }

        [RelayCommand]
        private void Run()
        {
            if (_activeSheet == null || Views.Count == 0)
                return;

            if (IsMultipleTitleBlocksWarning)
            {
                Log.Add(new LogEntry(LogLevel.Warning, "Run blocked — sheet has multiple title blocks. Resolve to a single title block and Refresh."));
                return;
            }

            IsBusy = true;
            Log.Add(new LogEntry(LogLevel.Info, "Run started."));

            _handler.Action = SheetAutoRearrangeAction.RunRearrange;
            _handler.TargetSheet = _activeSheet;
            _handler.ItemsToProcess = Views.Where(v => TypeIsVisible(v.ViewType)).ToList();
            // V030: the two never-implemented overflow modes are gone from the window; Run always places what fits.
            _handler.OverflowHandlingMode = OverflowHandlingMode.PlaceWhatsPlaceable;
            _handler.GapSettings = GapSettings;
            _handler.RowToleranceMm = RowToleranceMm;
            _handler.RowAlignment = RowAlignment;
            _handler.ColumnAlignmentH = ColumnAlignH;
            _handler.ColumnAlignmentV = ColumnAlignV;
            _handler.RowFillStrategy = RowFillStrategy;
            _handler.LeftoverGroupMode = LeftoverGroupMode;
            _handler.ColumnToleranceMm = ColumnToleranceMm;
            _handler.WithinColumnAlign = WithinColumnAlign;
            _handler.EnablePriorityGroups = EnablePriorityGroups;
            _handler.PriorityOrder = PriorityGroups.Select(g => g.ViewType).ToList();
            _handler.AlignDistribute = BuildAlignDistribute();
            _handler.Log = Log;

            _externalEvent.Raise();
        }

        [RelayCommand]
        private void ExportLog()
        {
            if (Log.Count == 0)
                return;

            string fullPath = LogExportHelper.SaveToDefaultFolder(Log);
            LastLogFilePath = fullPath;
            Log.Add(new LogEntry(LogLevel.Success, $"Log exported to {fullPath}."));
        }

        [RelayCommand]
        private void CopyAllLog()
        {
            var text = string.Join(System.Environment.NewLine, Log.Select(l => l.ToString()));
            System.Windows.Clipboard.SetText(text);
        }

        [RelayCommand]
        private void ClearLog() => Log.Clear();

        private void OnHandlerCompleted()
        {
            if (_handler.Detached)
            {
                // Window closed and DocumentChanged unhooked: release the event after this Execute returns.
                _dispatcher.BeginInvoke(new System.Action(Cleanup));
                return;
            }

            _dispatcher.Invoke(() =>
            {
                IsBusy = false;

                switch (_handler.Action)
                {
                    case SheetAutoRearrangeAction.LoadViewsOnSheet:
                        Views = new ObservableCollection<ViewOnSheetItem>(_handler.LoadedItems ?? new());
                        ApplyDetectionResultToState();
                        RebuildPriorityGroups();
                        UpdateMetrics();
                        Log.Add(new LogEntry(LogLevel.Info, $"Loaded {Views.Count} view(s) from sheet."));
                        break;

                    case SheetAutoRearrangeAction.RunRearrange:
                        var result = _handler.LastRunResult;
                        if (result != null)
                        {
                            TotalViewsMetric = result.TotalViews;
                            SelectedMetric = result.Selected;
                            RemovedMetric = result.Removed;
                            PlacedOkMetric = result.PlacedSuccessfully;
                            FailedToFitMetric = result.FailedToFit;
                            _lastRunText = result.Success
                                ? $"Done: {result.PlacedSuccessfully} placed, {result.Removed} removed, {result.FailedToFit} did not fit"
                                : "Run did not finish. See the activity log.";
                            SummaryText = _lastRunText;

                            string fullPath = LogExportHelper.SaveToDefaultFolder(Log);
                            LastLogFilePath = fullPath;

                            // V011 NEW: persist settings after every Run, in
                            // addition to on window Closing — survives a
                            // crash/force-close between Run and normal close.
                            SaveSettings();
                        }

                        // Only viewports actually sent to RearrangeEngine (i.e. of a
                        // currently visible view type) could have been deleted by
                        // this Run — an unchecked row hidden by the type filter was
                        // never in ItemsToProcess and still exists on the sheet.
                        var removedIds = new System.Collections.Generic.HashSet<ElementId>(
                            Views.Where(v => TypeIsVisible(v.ViewType) && !v.IsChecked).Select(v => v.ViewportId));
                        if (removedIds.Count > 0)
                        {
                            Views = new ObservableCollection<ViewOnSheetItem>(
                                Views.Where(v => !removedIds.Contains(v.ViewportId)));
                        }

                        UpdateMetrics();
                        break;
                }
            });
        }

        private void ApplyDetectionResultToState()
        {
            _region = _handler.Region;

            // Region = title block box inset by the margins sent with the load; undo that to get the box itself.
            _titleBlockRect = null;
            if (_region != null && !_handler.MultipleTitleBlocksFound)
            {
                var r = _region.LargeRect;
                // "margins too large — ignored": the region already is the title block box.
                double mm = _region.DisplayText.Contains("ignored") ? 0 : 1.0 / 304.8;
                _titleBlockRect = new RectFeet(
                    new XYZ(r.Min.X - _loadMargins.Left * mm, r.Min.Y - _loadMargins.Bottom * mm, 0),
                    new XYZ(r.Max.X + _loadMargins.Right * mm, r.Max.Y + _loadMargins.Top * mm, 0));
            }
            IsMultipleTitleBlocksWarning = _handler.MultipleTitleBlocksFound;

            if (_handler.MultipleTitleBlocksFound)
            {
                Log.Add(new LogEntry(LogLevel.Warning, "Sheet has multiple title blocks — Run is disabled until resolved to a single title block."));
                return;
            }

            if (_handler.NoTitleBlockFound || _region == null)
            {
                Log.Add(new LogEntry(LogLevel.Warning,
                    _handler.NoTitleBlockFound
                        ? "No title block found on sheet — Run will be blocked until one is added."
                        : "Title block geometry could not be read — Run will be blocked."));
                return;
            }

            Log.Add(new LogEntry(LogLevel.Info, "Title block detected — usable area computed from its bounding box, inset by configured margins."));
        }

        private void UpdateMetrics()
        {
            TotalViewsMetric = Views.Count;
            SelectedMetric = Views.Count(v => v.IsChecked);
        }
    }
}
