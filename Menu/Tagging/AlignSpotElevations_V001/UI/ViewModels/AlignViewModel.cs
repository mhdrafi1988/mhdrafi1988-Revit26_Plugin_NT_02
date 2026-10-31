using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Models;
using Revit26_Plugin_Revised.AlignSpotElevations.V001.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Revit26_Plugin_Revised.AlignSpotElevations.V001.UI.ViewModels
{
    /// <summary>One line of the preview list.</summary>
    public sealed class PreviewRow
    {
        /// <summary>Tag label.</summary>
        public string Label { get; set; }

        /// <summary>Position now (mm, along the aligned axis, relative to the first tag).</summary>
        public string Before { get; set; }

        /// <summary>Position after the move.</summary>
        public string After { get; set; }

        /// <summary>Distance moved, e.g. "+120 mm", or "stays".</summary>
        public string Moves { get; set; }
    }

    /// <summary>Options, live preview and result of the Align Spot Elevations window.</summary>
    public sealed class AlignViewModel : ObservableObject
    {
        private const double FeetToMm = 304.8;
        private readonly IReadOnlyList<SpotItem> _items;
        private double _origin;

        private bool _isVertical = true;
        private AlignTarget _target = AlignTarget.Average;
        private double _toleranceMm = 5;
        private SpotItem _reference;
        private string _summary = string.Empty;
        private bool _canApply;

        /// <summary>Creates the view model for the readable tags.</summary>
        public AlignViewModel(IReadOnlyList<SpotItem> items, IReadOnlyList<string> skipped)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
            Skipped = skipped ?? new List<string>();
            _reference = items.FirstOrDefault();
            Refresh();
        }

        /// <summary>Tags that can be aligned.</summary>
        public IReadOnlyList<SpotItem> Items => _items;

        /// <summary>Skipped tags with reasons.</summary>
        public IReadOnlyList<string> Skipped { get; }

        /// <summary>True when something was skipped.</summary>
        public bool HasSkipped => Skipped.Count > 0;

        /// <summary>Preview rows.</summary>
        public ObservableCollection<PreviewRow> Rows { get; } = new ObservableCollection<PreviewRow>();

        /// <summary>Vertical (one column) when true, horizontal (one row) when false.</summary>
        public bool IsVertical
        {
            get => _isVertical;
            set { if (SetProperty(ref _isVertical, value)) { OnPropertyChanged(nameof(IsHorizontal)); Refresh(); } }
        }

        /// <summary>Opposite of <see cref="IsVertical"/>, for the radio button.</summary>
        public bool IsHorizontal
        {
            get => !_isVertical;
            set { if (value) IsVertical = false; }
        }

        /// <summary>Selected target mode.</summary>
        public AlignTarget Target
        {
            get => _target;
            set
            {
                if (!SetProperty(ref _target, value)) return;
                OnPropertyChanged(nameof(IsAverage)); OnPropertyChanged(nameof(IsMostCommon));
                OnPropertyChanged(nameof(IsMedian)); OnPropertyChanged(nameof(IsMin));
                OnPropertyChanged(nameof(IsMax)); OnPropertyChanged(nameof(IsReference));
                Refresh();
            }
        }

        /// <summary>Radio: Average.</summary>
        public bool IsAverage { get => _target == AlignTarget.Average; set { if (value) Target = AlignTarget.Average; } }

        /// <summary>Radio: Most common.</summary>
        public bool IsMostCommon { get => _target == AlignTarget.MostCommon; set { if (value) Target = AlignTarget.MostCommon; } }

        /// <summary>Radio: Median.</summary>
        public bool IsMedian { get => _target == AlignTarget.Median; set { if (value) Target = AlignTarget.Median; } }

        /// <summary>Radio: Min.</summary>
        public bool IsMin { get => _target == AlignTarget.Min; set { if (value) Target = AlignTarget.Min; } }

        /// <summary>Radio: Max.</summary>
        public bool IsMax { get => _target == AlignTarget.Max; set { if (value) Target = AlignTarget.Max; } }

        /// <summary>Radio: Reference tag.</summary>
        public bool IsReference { get => _target == AlignTarget.Reference; set { if (value) Target = AlignTarget.Reference; } }

        /// <summary>Tolerance in millimetres for "Most common".</summary>
        public double ToleranceMm
        {
            get => _toleranceMm;
            set { if (SetProperty(ref _toleranceMm, Math.Max(0, value))) Refresh(); }
        }

        /// <summary>The reference tag.</summary>
        public SpotItem Reference
        {
            get => _reference;
            set { if (SetProperty(ref _reference, value)) Refresh(); }
        }

        /// <summary>One-line result under the options.</summary>
        public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

        /// <summary>True when at least one tag will move.</summary>
        public bool CanApply { get => _canApply; private set => SetProperty(ref _canApply, value); }

        /// <summary>Axis used.</summary>
        public AlignDirection Direction => _isVertical ? AlignDirection.Vertical : AlignDirection.Horizontal;

        /// <summary>The moves the current options produce (feet).</summary>
        public List<PlannedMove> Moves { get; private set; } = new List<PlannedMove>();

        private void Refresh()
        {
            Rows.Clear();
            Moves = new List<PlannedMove>();
            if (_items.Count == 0) { Summary = "No spot elevations to align."; CanApply = false; return; }
            if (_items.Count == 1) { Summary = "Select at least two spot elevations."; CanApply = false; return; }
            if (_target == AlignTarget.Reference && _reference == null) { Summary = "Choose a reference spot."; CanApply = false; return; }

            double tol = _toleranceMm / FeetToMm;
            TargetResult target = AlignEngine.ChooseTarget(_items, Direction, _target, tol, _reference);
            Moves = AlignEngine.Plan(_items, Direction, target.Value);
            _origin = AlignEngine.Axis(_items[0], Direction);

            foreach (SpotItem item in _items)
            {
                double now = AlignEngine.Axis(item, Direction);
                PlannedMove move = Moves.FirstOrDefault(m => m.Item == item);
                Rows.Add(new PreviewRow
                {
                    Label = item.Label,
                    Before = Mm(now - _origin),
                    After = Mm(target.Value - _origin),
                    Moves = move == null ? "stays" : $"{(move.Delta >= 0 ? "+" : "")}{move.Delta * FeetToMm:0} mm"
                });
            }

            string axis = _isVertical ? "one column" : "one row";
            Summary = $"{Moves.Count} of {_items.Count} tags will move to {axis}"
                + (target.FellBackToAverage ? " — no tags share a position, so the average is used." : ".")
                + (_target == AlignTarget.MostCommon && !target.FellBackToAverage ? $" {target.SharedCount} tags already share it." : string.Empty);
            CanApply = Moves.Count > 0;
        }

        private static string Mm(double feet) => $"{feet * FeetToMm:0.#} mm";
    }
}
