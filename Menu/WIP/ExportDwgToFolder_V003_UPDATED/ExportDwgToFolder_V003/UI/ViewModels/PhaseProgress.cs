using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin.ExportDwgToFolder.V003.UI.ViewModels
{
    /// <summary>State of one progress bar in the footer.</summary>
    public enum PhaseState
    {
        /// <summary>Not started (grey).</summary>
        Pending,

        /// <summary>Running (blue).</summary>
        Running,

        /// <summary>Finished cleanly (green).</summary>
        Done,

        /// <summary>Finished with failures or stopped on an error (amber).</summary>
        Warning
    }

    /// <summary>
    /// One footer progress bar (Scanning / Loading list / Exporting): count, percentage and state.
    /// </summary>
    public partial class PhaseProgress : ObservableObject
    {
        /// <summary>Creates a pending bar.</summary>
        public PhaseProgress(string label)
        {
            Label = label;
        }

        /// <summary>Bar caption.</summary>
        public string Label { get; }

        /// <summary>Items done.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Percent), nameof(CountText), nameof(PercentText))]
        private int current;

        /// <summary>Items in total.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Percent), nameof(CountText), nameof(PercentText))]
        private int total;

        /// <summary>Bar state (drives its colour).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Percent), nameof(CountText), nameof(PercentText))]
        private PhaseState state;

        /// <summary>0–100. A finished phase with nothing to do shows 100.</summary>
        public double Percent
        {
            get
            {
                if (Total > 0) return System.Math.Min(100.0, Current * 100.0 / Total);
                return State == PhaseState.Done || State == PhaseState.Warning ? 100 : 0;
            }
        }

        /// <summary>"12 / 40", or "–" while pending.</summary>
        public string CountText => State == PhaseState.Pending ? "–" : $"{Current:N0} / {Total:N0}";

        /// <summary>"30%", or "–" while pending.</summary>
        public string PercentText => State == PhaseState.Pending ? "–" : $"{Percent:0}%";

        /// <summary>Back to grey / empty.</summary>
        public void Reset()
        {
            State = PhaseState.Pending;
            Current = 0;
            Total = 0;
        }

        /// <summary>Starts the bar at 0 of <paramref name="totalItems"/>.</summary>
        public void Start(int totalItems)
        {
            Current = 0;
            Total = totalItems;
            State = PhaseState.Running;
        }

        /// <summary>Updates the count.</summary>
        public void Report(int done, int totalItems)
        {
            Total = totalItems;
            Current = done;
            if (State == PhaseState.Pending) State = PhaseState.Running;
        }

        /// <summary>
        /// Marks the bar finished: green, or amber when <paramref name="withWarnings"/>.
        /// <paramref name="stopped"/> keeps the count where it stopped instead of filling the bar.
        /// </summary>
        public void Complete(bool withWarnings = false, bool stopped = false)
        {
            if (!stopped) Current = Total;
            State = withWarnings || stopped ? PhaseState.Warning : PhaseState.Done;
        }
    }
}
