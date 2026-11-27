using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.SectionRoofDetailLines.V001.Core.Models;
using System;

namespace Revit26_Plugin_Revised.SectionRoofDetailLines.V001.UI.ViewModels
{
    /// <summary>How a section row reads: plain, good, worth noting, or not usable.</summary>
    public enum SectionRowStatus
    {
        /// <summary>Nothing to say.</summary>
        None,

        /// <summary>The last run drew lines here.</summary>
        Done,

        /// <summary>Usable, with something the user should know (the crop box is off, nothing is cut).</summary>
        Note,

        /// <summary>Cannot be drawn in — owned by someone else, or changed in central.</summary>
        Blocked
    }

    /// <summary>One section view in the list on the left: whether it is in the run, and what it holds.</summary>
    public partial class SectionRowViewModel : ObservableObject
    {
        private readonly Action _onTickChanged;

        /// <summary>Creates a row for <paramref name="target"/>. <paramref name="onTickChanged"/>
        /// lets the window update its header cards when the tick changes.</summary>
        public SectionRowViewModel(SectionTarget target, Action onTickChanged)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            _onTickChanged = onTickChanged;
            _isTicked = target.CanDraw;
        }

        /// <summary>False while the section search hides this row. A hidden ticked row is still drawn.</summary>
        [ObservableProperty]
        private bool _isVisible = true;

        /// <summary>The section as read from the model.</summary>
        public SectionTarget Target { get; }

        /// <summary>Element id of the section view.</summary>
        public long ViewKey => Target.ViewKey;

        /// <summary>The view's name.</summary>
        public string Name => Target.ViewName;

        /// <summary>Its detail number on the sheet, or "—".</summary>
        public string Chip => string.IsNullOrWhiteSpace(Target.DetailNumber) ? "—" : Target.DetailNumber;

        /// <summary>True when the view can take lines.</summary>
        public bool CanDraw => Target.CanDraw;

        /// <summary>Scale and what the view shows, for the row's second line.</summary>
        public string Meta
        {
            get
            {
                string scale = Target.Scale > 0 ? $"1:{Target.Scale}" : "scale unknown";
                return $"{scale} · {Target.RoofCount} roof(s), {Target.WallCount} wall(s), {Target.ElementCount} element(s) shown";
            }
        }

        [ObservableProperty]
        private bool _isTicked;

        /// <summary>Lines the last run drew in this view; null before any run.</summary>
        [ObservableProperty]
        private int? _linesDrawn;

        /// <summary>The row's one-line status, or empty.</summary>
        public string Status
        {
            get
            {
                if (!CanDraw) return $"Cannot draw here — {Target.LockReason}.";
                if (LinesDrawn.HasValue)
                    return LinesDrawn.Value > 0
                        ? $"Last run: {LinesDrawn.Value} line(s) drawn."
                        : "Last run: nothing to draw here.";
                if (Target.CropInactive)
                    return "The crop box is off — the run uses the crop box as its extent, so the view may show more.";
                if (Target.RoofCount == 0)
                    return "No roof is shown here, so wall lines have nothing to sit next to.";
                return string.Empty;
            }
        }

        /// <summary>True when <see cref="Status"/> has something to show.</summary>
        public bool HasStatus => !string.IsNullOrEmpty(Status);

        /// <summary>How the status should read.</summary>
        public SectionRowStatus StatusKind
        {
            get
            {
                if (!CanDraw) return SectionRowStatus.Blocked;
                if (LinesDrawn.HasValue && LinesDrawn.Value > 0) return SectionRowStatus.Done;
                if (HasStatus) return SectionRowStatus.Note;
                return SectionRowStatus.None;
            }
        }

        partial void OnIsTickedChanged(bool value)
        {
            if (value && !CanDraw)
            {
                // A locked view cannot be drawn in, so the tick does not stick.
                IsTicked = false;
                return;
            }
            _onTickChanged?.Invoke();
        }

        partial void OnLinesDrawnChanged(int? value) => RefreshStatus();

        /// <summary>Tells the view to read the status properties again.</summary>
        public void RefreshStatus()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(HasStatus));
            OnPropertyChanged(nameof(StatusKind));
            OnPropertyChanged(nameof(Meta));
        }
    }
}
