using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Layout;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Models;
using Revit26_Plugin_Revised.SheetAlignDistribute.V001.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetAlignDistribute.V001.UI.ViewModels
{
    /// <summary>One box in a suggestion button's sketch, already scaled to the sketch canvas.</summary>
    public sealed class SketchBoxViewModel
    {
        /// <summary>Left edge on the canvas.</summary>
        public double Left { get; init; }

        /// <summary>Top edge on the canvas.</summary>
        public double Top { get; init; }

        /// <summary>Width on the canvas.</summary>
        public double Width { get; init; }

        /// <summary>Height on the canvas.</summary>
        public double Height { get; init; }

        /// <summary>What the plan does with this element; picks its colour.</summary>
        public AlignStatus Status { get; init; }
    }

    /// <summary>One "Suggested layouts" button: what it would do, and whether it is the one picked.</summary>
    public sealed partial class SuggestionCardViewModel : ObservableObject
    {
        /// <summary>Width of the sketch canvas.</summary>
        public const double SketchWidth = 140;

        /// <summary>Height of the sketch canvas.</summary>
        public const double SketchHeight = 54;

        /// <summary>Wraps <paramref name="suggestion"/>.</summary>
        public SuggestionCardViewModel(LayoutSuggestion suggestion)
        {
            Suggestion = suggestion ?? throw new ArgumentNullException(nameof(suggestion));
            Sketch = BuildSketch(suggestion.Outcome);
        }

        /// <summary>The suggestion behind this button.</summary>
        public LayoutSuggestion Suggestion { get; }

        /// <summary>The combination the button loads.</summary>
        public LayoutCombo Combo => Suggestion.Combo;

        /// <summary>True while this button's combination is what the dropdowns hold.</summary>
        [ObservableProperty] private bool _isSelected;

        /// <summary>"1", "2", … as shown in the button's circle.</summary>
        public string RankText => Suggestion.Rank.ToString();

        /// <summary>True for the top-ranked suggestion, which carries the "Best fit" pill.</summary>
        public bool IsBest => Suggestion.Rank == 1;

        /// <summary>The combination's name, e.g. "Bottom + Left · gaps kept".</summary>
        public string Title => Combo.Title;

        /// <summary>Short chip label, e.g. "Bottom + Left" or "Bottom + Left · even".</summary>
        public string ShortTitle
        {
            get
            {
                string text = Combo.Title;
                int dot = text.IndexOf(" · ", StringComparison.Ordinal);
                string head = dot < 0 ? text : text.Substring(0, dot);
                bool even = Combo.Horizontal == DistributeMode.EvenGaps || Combo.Vertical == DistributeMode.EvenGaps;
                bool fit = Combo.Horizontal == DistributeMode.FitMargins || Combo.Vertical == DistributeMode.FitMargins;
                return head + (fit ? " · fit" : even ? " · even" : "");
            }
        }

        /// <summary>Chip numbers: "5 · 5.7 mm" (moves · largest move).</summary>
        public string ChipStats => $"{Suggestion.Outcome.Moves} · {Suggestion.Outcome.LargestMoveMm:0.0} mm";

        /// <summary>How many views overflow, for the chip's orange badge.</summary>
        public string OverflowCountText => Suggestion.Outcome.Overflow.ToString();

        /// <summary>"5 moves · max 5.7 mm".</summary>
        public string MovesText
        {
            get
            {
                var o = Suggestion.Outcome;
                return $"{o.Moves} move{(o.Moves == 1 ? "" : "s")} · max {o.LargestMoveMm:0.0} mm";
            }
        }

        /// <summary>"2 in place".</summary>
        public string InPlaceText => $"{Suggestion.Outcome.InPlace} in place";

        /// <summary>True when some views would overflow, so Apply will ask what to do with them.</summary>
        public bool HasOverflow => Suggestion.Outcome.Overflow > 0;

        /// <summary>"1 over limit · 2 cross the margins", or empty.</summary>
        public string OverflowText
        {
            get
            {
                var o = Suggestion.Outcome;
                var parts = new List<string>();
                if (o.OverLimit > 0) parts.Add($"{o.OverLimit} over limit");
                if (o.CrossMargins > 0) parts.Add($"{o.CrossMargins} cross{(o.CrossMargins == 1 ? "es" : "")} the margins");
                return string.Join(" · ", parts);
            }
        }

        /// <summary>One plain line under the numbers, naming any combinations merged into this one.</summary>
        public string FootText
        {
            get
            {
                string text = Combo.Description;
                var same = Suggestion.SameResult;
                if (same.Count > 0)
                    text += $" Same result as {same[0].Title}" + (same.Count > 1 ? $" and {same.Count - 1} more." : ".");
                return text;
            }
        }

        /// <summary>Tooltip: the full breakdown.</summary>
        public string ToolTipText =>
            $"{Title}\n{MovesText}, {InPlaceText}" +
            (HasOverflow ? $"\n{OverflowText} — Apply asks whether to place them anyway." : "") +
            "\nClick to preview it. Nothing moves until you press Apply.";

        /// <summary>Where every element ends up, drawn small.</summary>
        public IReadOnlyList<SketchBoxViewModel> Sketch { get; }

        private static IReadOnlyList<SketchBoxViewModel> BuildSketch(CandidateOutcome outcome)
        {
            if (outcome?.Sketch == null || outcome.Sketch.Count == 0) return Array.Empty<SketchBoxViewModel>();

            LayoutRect bounds = outcome.Frame ?? outcome.Sketch.Select(s => s.Target).Aggregate((a, b) => a.Union(b));
            if (!bounds.IsValid) return Array.Empty<SketchBoxViewModel>();

            const double pad = 3;
            double scale = Math.Min((SketchWidth - 2 * pad) / bounds.Width, (SketchHeight - 2 * pad) / bounds.Height);
            double offsetX = (SketchWidth - bounds.Width * scale) / 2;
            double offsetY = (SketchHeight - bounds.Height * scale) / 2;

            return outcome.Sketch.Select(s => new SketchBoxViewModel
            {
                Left = offsetX + (s.Target.MinX - bounds.MinX) * scale,
                // Sheet Y rises upward; the canvas's falls.
                Top = offsetY + (bounds.MaxY - s.Target.MaxY) * scale,
                Width = Math.Max(1, s.Target.Width * scale),
                Height = Math.Max(1, s.Target.Height * scale),
                Status = s.Status
            }).ToList();
        }
    }
}
