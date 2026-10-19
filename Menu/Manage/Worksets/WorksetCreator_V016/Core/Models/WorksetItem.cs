// ==============================================
// File: WorksetItem.cs
// Layer: Core/Models  (no Revit API — CLAUDE.md)
// Workset Creator V016.
//
// Changes vs V015:
//   NEW  LinkTypeId — the row's identity is now the RevitLinkType, not its file
//        name. Two links with the same file name from different folders are two
//        rows (V015's Distinct(name) collapsed them into one).
//   NEW  LoadState + StatusText — Grid 3 no longer says "Unloaded / not placed"
//        for everything; it says which one it actually is.
//   CHG  GridCategory doc comments: NoInstances now means genuinely nothing
//        placed, because an unloaded-but-placed link is counted properly and
//        lands in NeedsWorkset.
//
// Added later in V016 (duplicate prevention):
//   NEW  IsIfc — the link came in through an .ifc.rvt shadow file. Drives the
//        row colour only; it never changes what the run does.
//   NEW  DisplayName — the file name WITH its extension, which is what the
//        Linked file column shows. LinkName stays the stem, because that is the
//        anchor the matcher searches for and the value {name} resolves to.
//   NEW  The match block — MatchedWorkset*, MatchConfidence, CandidateCount,
//        MatchExplanation — the evidence behind the suggested action.
//   NEW  SelectedAction / AvailableActions / IsReviewResolved / FinalWorksetName.
//        FinalWorksetName is the single answer to "what workset will this link
//        end up in", which is what the pre-flight gate checks and the request
//        carries. Nothing downstream re-derives it.
// ==============================================

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.WorksetCreator.V016.Core.Models
{
    public enum WorksetGridCategory
    {
        /// <summary>Grid 1 — proposed workset exists and every instance is already in it.</summary>
        AlreadyAssigned,

        /// <summary>
        /// Grid 2 — the link has instances in the model (loaded or unloaded) that
        /// are not all in the proposed workset.
        /// </summary>
        NeedsWorkset,

        /// <summary>
        /// Grid 3 — no RevitLinkInstance of this type exists in the model at all.
        /// The workset can be created, but there is nothing to assign to it until
        /// someone places the link.
        /// </summary>
        NoInstances
    }

    /// <summary>
    /// One entry in a row's Action picker. Carries the action and the workset name
    /// it would produce, so the row can show the consequence rather than the verb.
    /// </summary>
    public sealed class WorksetActionOption
    {
        public WorksetAction Action { get; set; }
        public string Label  { get; set; } = string.Empty;

        /// <summary>The workset this option ends in. Empty for Skip.</summary>
        public string TargetName { get; set; } = string.Empty;

        /// <summary>The matched workset's id when this option acts on one; 0 otherwise.</summary>
        public int TargetWorksetId { get; set; }

        public string Tooltip { get; set; } = string.Empty;

        public override string ToString() => Label;
    }

    public partial class WorksetItem : ObservableObject
    {
        [ObservableProperty] private int    serialNumber;

        /// <summary>ElementId.IntegerValue of the RevitLinkType this row stands for.</summary>
        [ObservableProperty] private int    linkTypeId;

        /// <summary>File name without its final extension — the matcher's anchor and {name}.</summary>
        [ObservableProperty] private string linkName;

        /// <summary>
        /// File name as Revit holds it, extension included — "WAMI-AR-Z1-Tower.rvt",
        /// "Z3_IFC_21-08-2026.ifc.rvt". What the Linked file column shows, so a double
        /// extension is visible rather than silently trimmed.
        /// </summary>
        [ObservableProperty] private string displayName;

        [ObservableProperty] private string currentWorksetName  = "None";
        [ObservableProperty] private bool   isMixedWorkset;
        [ObservableProperty] private string proposedWorksetName;
        [ObservableProperty] private string proposedWorksetTooltip;
        [ObservableProperty] private bool   isSelected;
        [ObservableProperty] private bool   hasInstances        = true;
        [ObservableProperty] private bool   isExactMatchAssigned;
        [ObservableProperty] private bool   isExistingWorkset;
        [ObservableProperty] private string existingWorksetName;
        [ObservableProperty] private int    instanceCount;
        [ObservableProperty] private WorksetGridCategory gridCategory;

        /// <summary>Loaded / Unloaded / NotFound — straight from the scan.</summary>
        [ObservableProperty] private LinkLoadState loadState = LinkLoadState.Loaded;

        /// <summary>ACC Docs status of the link — the green radio dot in the ACC column.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AccSortOrder))]
        private AccLinkState accState = AccLinkState.NotAcc;

        /// <summary>Sort key for the ACC column — ACC problems first, non-ACC links last.</summary>
        public int AccSortOrder => AccLinkStatus.SortOrder(AccState);

        // ── IFC ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Revit linked an .ifc through a generated "&lt;file&gt;.ifc.rvt" shadow model.
        /// Row colour only — an IFC link is created, matched and assigned exactly
        /// like any other Revit link once it is listed.
        /// </summary>
        [ObservableProperty] private bool isIfc;

        // ── Match block ──────────────────────────────────────────────────────

        /// <summary>Existing workset the matcher believes is this link's. Empty when none.</summary>
        [ObservableProperty] private string matchedWorksetName = string.Empty;

        /// <summary>WorksetId.IntegerValue of that workset; 0 when there is no match.</summary>
        [ObservableProperty] private int matchedWorksetId;

        /// <summary>
        /// Elements in the matched workset. Shown in the tooltip so a workset holding
        /// more than this link's instances is visible before a rename is accepted.
        /// </summary>
        [ObservableProperty] private int matchedWorksetElementCount;

        [ObservableProperty] private MatchConfidence matchConfidence = MatchConfidence.None;

        /// <summary>How many existing worksets could be this link's. &gt;1 means the model already duplicates it.</summary>
        [ObservableProperty] private int candidateCount;

        /// <summary>Why the matcher reached its verdict — tooltip and log text.</summary>
        [ObservableProperty] private string matchExplanation = string.Empty;

        /// <summary>
        /// True when the matcher will not decide for the user: a residue too rare to
        /// trust, or more than one candidate workset.
        /// </summary>
        [ObservableProperty] private bool needsReview;

        /// <summary>
        /// Set when the user picks an action on a review row. Until then the row is
        /// held out of the run and the pre-flight gate blocks on it.
        /// </summary>
        [ObservableProperty] private bool isReviewResolved;

        // ── Action ───────────────────────────────────────────────────────────

        [ObservableProperty] private WorksetAction selectedAction = WorksetAction.Create;

        /// <summary>The picker's entries — built per row, since the options depend on the match.</summary>
        public ObservableCollection<WorksetActionOption> AvailableActions { get; } = new();

        private WorksetActionOption _selectedActionOption;

        /// <summary>
        /// Two-way bound to the Action ComboBox. Assigning it is the only way the UI
        /// changes a row's action, so resolving a review row and recomputing the final
        /// name happen together and cannot drift apart.
        /// </summary>
        public WorksetActionOption SelectedActionOption
        {
            get => _selectedActionOption;
            set
            {
                if (!SetProperty(ref _selectedActionOption, value)) return;
                if (value == null) return;

                SelectedAction = value.Action;

                // A deliberate choice on a review row is the user's decision.
                if (NeedsReview) IsReviewResolved = true;

                OnPropertyChanged(nameof(SelectedTargetWorksetId));
                OnPropertyChanged(nameof(SelectedTargetName));
                RefreshFinalWorksetName();
            }
        }

        /// <summary>
        /// The workset the CHOSEN option acts on — not necessarily the best match.
        /// On an ambiguous row the picker offers one option per candidate workset, so
        /// this is how "pick the survivor" actually reaches the request. 0 when the
        /// option creates a workset or skips the link.
        /// </summary>
        public int SelectedTargetWorksetId => _selectedActionOption?.TargetWorksetId ?? 0;

        /// <summary>The chosen option's workset name. Empty for Create and Skip.</summary>
        public string SelectedTargetName => _selectedActionOption?.TargetName ?? string.Empty;

        // ── Derived ──────────────────────────────────────────────────────────

        /// <summary>
        /// The one workset this link ends up in. The pre-flight gate checks this and
        /// the request carries it — nothing downstream recomputes it.
        /// </summary>
        [ObservableProperty] private string finalWorksetName = string.Empty;

        /// <summary>
        /// Recomputes <see cref="FinalWorksetName"/> from the action. Create, Rename and
        /// Reuse all end at the pattern name; Adopt keeps the matched workset's name;
        /// Skip ends nowhere.
        /// </summary>
        public void RefreshFinalWorksetName()
        {
            FinalWorksetName = SelectedAction switch
            {
                WorksetAction.Skip => string.Empty,

                // Adopt keeps the chosen workset's own name. On an ambiguous row that is
                // whichever candidate the user picked, which is why it reads the option
                // rather than the best match.
                WorksetAction.Adopt => !string.IsNullOrWhiteSpace(SelectedTargetName)
                                           ? SelectedTargetName
                                           : MatchedWorksetName ?? string.Empty,

                _ => ProposedWorksetName ?? string.Empty
            };
        }

        partial void OnProposedWorksetNameChanged(string value) => RefreshFinalWorksetName();

        partial void OnSelectedActionChanged(WorksetAction value)
        {
            OnPropertyChanged(nameof(ActionText));
            RefreshFinalWorksetName();
        }

        partial void OnMatchedWorksetNameChanged(string value) => OnPropertyChanged(nameof(HasMatch));

        partial void OnMatchConfidenceChanged(MatchConfidence value) =>
            OnPropertyChanged(nameof(MatchText));

        partial void OnCandidateCountChanged(int value) => OnPropertyChanged(nameof(MatchText));

        partial void OnLoadStateChanged(LinkLoadState value)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsUnloaded));
            OnPropertyChanged(nameof(LoadStateText));
        }

        partial void OnInstanceCountChanged(int value) => OnPropertyChanged(nameof(StatusText));

        public bool IsUnloaded => LoadState == LinkLoadState.Unloaded;

        public bool HasMatch => !string.IsNullOrWhiteSpace(MatchedWorksetName);

        /// <summary>The Existing match column.</summary>
        public string MatchText => MatchConfidence switch
        {
            MatchConfidence.None      => "— none",
            MatchConfidence.Ambiguous => $"{CandidateCount} candidates — conflict",
            _                         => MatchedWorksetName
        };

        /// <summary>Short label for the action, used where the picker is not shown.</summary>
        public string ActionText => SelectedAction switch
        {
            WorksetAction.Create => "Create new",
            WorksetAction.Reuse  => "Use existing",
            WorksetAction.Rename => "Rename → pattern",
            WorksetAction.Adopt  => "Keep existing name",
            WorksetAction.Skip   => "Skip this link",
            _                    => SelectedAction.ToString()
        };

        /// <summary>Short badge for Grid 2 — blank when the link is loaded and normal.</summary>
        public string LoadStateText => LoadState switch
        {
            LinkLoadState.Unloaded => "Unloaded",
            LinkLoadState.NotFound => "Not found",
            _                      => "Loaded"
        };

        /// <summary>
        /// Grid 3's Status column. V015 hard-coded "Unloaded / not placed" into the
        /// XAML because it could not tell the two apart; V016 can.
        /// </summary>
        public string StatusText => LoadState switch
        {
            LinkLoadState.NotFound => "Not found",
            LinkLoadState.Unloaded => "Unloaded · not placed",
            _                      => "Not placed"
        };

        /// <summary>
        /// Rebuilds the picker for this row's match and selects the suggested action.
        /// Called by the ViewModel after a scan; the row never builds its own options,
        /// because only the ViewModel knows the matcher's verdict.
        /// </summary>
        public void BuildActionOptions(IEnumerable<WorksetActionOption> options, WorksetAction suggested)
        {
            AvailableActions.Clear();
            foreach (WorksetActionOption option in options ?? Enumerable.Empty<WorksetActionOption>())
                AvailableActions.Add(option);

            WorksetActionOption pick =
                AvailableActions.FirstOrDefault(o => o.Action == suggested) ??
                AvailableActions.FirstOrDefault();

            // Assign the backing field directly: selecting the SUGGESTED action is the
            // tool's own proposal, not the user resolving a review row, so it must not
            // set IsReviewResolved.
            _selectedActionOption = pick;
            OnPropertyChanged(nameof(SelectedActionOption));
            OnPropertyChanged(nameof(SelectedTargetWorksetId));
            OnPropertyChanged(nameof(SelectedTargetName));

            if (pick != null) SelectedAction = pick.Action;
            RefreshFinalWorksetName();
        }
    }
}
