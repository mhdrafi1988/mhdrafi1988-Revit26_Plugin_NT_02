using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.BaseAndNearestLevel.V001.Core;

namespace Revit26_Plugin_Revised.BaseAndNearestLevel.V001.UI.ViewModels
{
    /// <summary>One element the re-host will move, as the confirmation dialog lists it.</summary>
    public sealed class RehostMove
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>"Floor: 200mm Slab".</summary>
        public string FamilyAndType { get; set; } = string.Empty;

        /// <summary>"GF +3400".</summary>
        public string From { get; set; } = string.Empty;

        /// <summary>"Level 01 -100".</summary>
        public string To { get; set; } = string.Empty;

        /// <summary>"Z stays 3400".</summary>
        public string Unchanged { get; set; } = string.Empty;
    }

    /// <summary>One element the re-host will leave alone, with the reason.</summary>
    public sealed class RehostSkipRow
    {
        /// <summary>Element id value.</summary>
        public long Id { get; set; }

        /// <summary>"Floor: 200mm Slab".</summary>
        public string FamilyAndType { get; set; } = string.Empty;

        /// <summary>Short label: "Shape-edited", "In a group", …</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>The full sentence.</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>True for a shape-edited element, which the dialog's tick can include.</summary>
        public bool IsShapeEdited { get; set; }
    }

    /// <summary>
    /// What the "Re-host to nearest level?" dialog shows: every move, every skip, and the shape-edited
    /// override. Ticking the override rebuilds both lists at once, so the button's count always matches
    /// what the run will do. Built on the WPF thread from the facts the last read produced.
    /// </summary>
    public sealed partial class RehostConfirmation : ObservableObject
    {
        private readonly List<ElementFacts> _facts;

        private RehostConfirmation(IEnumerable<ElementFacts> facts, bool includeShapeEdited)
        {
            _facts = (facts ?? Enumerable.Empty<ElementFacts>()).Where(f => f != null && f.IsChanged).ToList();
            _includeShapeEdited = includeShapeEdited;
            Rebuild();
        }

        /// <summary>Builds the confirmation from the facts of the ticked rows.</summary>
        public static RehostConfirmation Build(IEnumerable<ElementFacts> facts, bool includeShapeEdited) =>
            new(facts, includeShapeEdited);

        /// <summary>The elements that will be re-hosted.</summary>
        public ObservableCollection<RehostMove> Moves { get; } = new();

        /// <summary>The elements that will be skipped.</summary>
        public ObservableCollection<RehostSkipRow> Skips { get; } = new();

        /// <summary>Re-host shape-edited floors and roofs as well. The dialog's tick writes this back.</summary>
        [ObservableProperty] private bool _includeShapeEdited;

        partial void OnIncludeShapeEditedChanged(bool value) => Rebuild();

        /// <summary>True when any element is skipped.</summary>
        public bool HasSkips => Skips.Count > 0;

        /// <summary>True when any element is shape-edited, so the override tick is worth showing.</summary>
        public bool HasShapeEditedSkips => _facts.Any(f => f.ShapeEdited);

        /// <summary>"Re-host 5 elements", for the dialog's primary button.</summary>
        public string ActionText => Moves.Count == 1 ? "Re-host 1 element" : $"Re-host {Moves.Count} elements";

        /// <summary>One line above the list.</summary>
        public string Headline => Moves.Count == 0
            ? "No element can be re-hosted."
            : $"{Moves.Count} element(s) will be re-hosted onto their nearest level. Nothing moves: each one keeps its elevation.";

        /// <summary>"3 elements will be skipped", or empty when none is.</summary>
        public string SkipHeadline => Skips.Count switch
        {
            0 => string.Empty,
            1 => "1 element will be skipped",
            _ => $"{Skips.Count} elements will be skipped"
        };

        /// <summary>True when there is anything to re-host.</summary>
        public bool CanRun => Moves.Count > 0;

        private void Rebuild()
        {
            Moves.Clear();
            Skips.Clear();

            foreach (ElementFacts e in _facts)
            {
                RehostSkip block = BaseAndNearestRules.RehostBlockFor(e, IncludeShapeEdited);
                if (block == RehostSkip.None)
                {
                    Moves.Add(new RehostMove
                    {
                        Id = e.Id,
                        FamilyAndType = e.FamilyAndType,
                        From = $"{e.BaseLevelName} {ValueText.Offset(e.BaseOffsetMm)}",
                        To = $"{e.NearestLevelName} {ValueText.Offset(e.NearestOffsetMm)}",
                        Unchanged = $"Z stays {ValueText.Offset(e.ElevationMm).TrimStart('+')}"
                    });
                }
                else if (block != RehostSkip.NoChange)
                {
                    Skips.Add(new RehostSkipRow
                    {
                        Id = e.Id,
                        FamilyAndType = e.FamilyAndType,
                        Reason = ShortReason(block),
                        Detail = BaseAndNearestRules.RehostSkipText(block),
                        IsShapeEdited = block == RehostSkip.ShapeEdited
                    });
                }
            }

            OnPropertyChanged(nameof(HasSkips));
            OnPropertyChanged(nameof(HasShapeEditedSkips));
            OnPropertyChanged(nameof(ActionText));
            OnPropertyChanged(nameof(Headline));
            OnPropertyChanged(nameof(SkipHeadline));
            OnPropertyChanged(nameof(CanRun));
        }

        private static string ShortReason(RehostSkip skip) => skip switch
        {
            RehostSkip.ShapeEdited => "Shape-edited",
            RehostSkip.ReadOnlyLevel => "Read-only",
            RehostSkip.InGroup => "In a group",
            RehostSkip.OwnedByOther => "Checked out",
            RehostSkip.NoBaseLevel => "No base level",
            _ => "Skipped"
        };
    }
}
