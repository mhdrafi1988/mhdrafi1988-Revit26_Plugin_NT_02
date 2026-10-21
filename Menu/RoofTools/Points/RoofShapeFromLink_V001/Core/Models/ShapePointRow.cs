using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Models
{
    /// <summary>
    /// One shape-editing point of a host roof, with the two candidate elevations read from the
    /// linked roof above it. Z values are in the project's length unit (not feet) so the grid shows
    /// what the user types.
    /// </summary>
    public partial class ShapePointRow : ObservableObject
    {
        /// <summary>Source choice: top surface of the linked roof at the same X,Y.</summary>
        public const string SourceSurface = "Surface";

        /// <summary>Source choice: elevation of the nearest shape-editing point of the linked roof.</summary>
        public const string SourceNearest = "Nearest";

        /// <summary>Source shown once the user types a New Z by hand.</summary>
        public const string SourceManual = "Manual";

        private bool _suppress;

        /// <summary>Host roof element id.</summary>
        public long RoofId { get; init; }

        /// <summary>Host roof label, "Type name [id]".</summary>
        public string RoofLabel { get; init; } = "";

        /// <summary>1-based point number within its roof.</summary>
        public int Index { get; init; }

        /// <summary>Point X in feet (Revit internal), used to find the vertex again when applying.</summary>
        public double XFt { get; init; }

        /// <summary>Point Y in feet (Revit internal), used to find the vertex again when applying.</summary>
        public double YFt { get; init; }

        /// <summary>Point X in the project length unit.</summary>
        public double X { get; init; }

        /// <summary>Point Y in the project length unit.</summary>
        public double Y { get; init; }

        /// <summary>Linked roof the candidates came from, "Link name · roof id"; empty when none was found.</summary>
        public string LinkedRoof { get; init; } = "";

        /// <summary>Z of the linked roof top face at the point X,Y, or null.</summary>
        public double? SurfaceZ { get; init; }

        /// <summary>Z of the nearest shape-editing point of the linked roof, or null.</summary>
        public double? NearestZ { get; init; }

        /// <summary>Horizontal distance to that nearest point, or null.</summary>
        public double? NearestDistance { get; init; }

        /// <summary>Note on how the candidates were found (snapped to edge, linked roof below, none found).</summary>
        public string Note { get; init; } = "";

        /// <summary>Longest delta bar in pixels.</summary>
        private const double MaxBarWidth = 60;

        /// <summary>Largest absolute change in the grid; the VM keeps it current so delta bars share one scale.</summary>
        public static double DeltaScale { get; set; } = 1;

        /// <summary>Elevation of the point in the model now.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Delta), nameof(DeltaText), nameof(DeltaIsUp), nameof(DeltaBar))]
        private double _currentZ;

        /// <summary>True when the row is written back by Apply.</summary>
        [ObservableProperty] private bool _include;

        /// <summary>Surface, Nearest or Manual.</summary>
        [ObservableProperty] private string _source = SourceSurface;

        /// <summary>Fixed number subtracted from the chosen candidate; starts at the global value.</summary>
        [ObservableProperty] private double _offset;

        /// <summary>Elevation that Apply will set; editable.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Delta), nameof(DeltaText), nameof(DeltaIsUp), nameof(DeltaBar))]
        private double? _newZ;

        /// <summary>OK, Snapped (edge of the linked face), Below (linked roof lower than the point) or No link.</summary>
        public string Status =>
            !SurfaceZ.HasValue && !NearestZ.HasValue ? "No link"
            : Note.IndexOf("snapped", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "Snapped"
            : Note.IndexOf("below", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "Below"
            : "OK";

        /// <summary>New Z minus Current Z, or null when there is no New Z.</summary>
        public double? Delta => NewZ.HasValue ? NewZ.Value - CurrentZ : null;

        /// <summary>Signed change as text, e.g. "+12.500"; an em dash when none.</summary>
        public string DeltaText => Delta.HasValue ? (Delta.Value >= 0 ? "+" : "") + Delta.Value.ToString("F3") : "—";

        /// <summary>True when the point moves up (or stays).</summary>
        public bool DeltaIsUp => !Delta.HasValue || Delta.Value >= 0;

        /// <summary>Width of the delta bar in pixels, scaled by <see cref="DeltaScale"/>.</summary>
        public double DeltaBar => Delta.HasValue ? System.Math.Max(1, System.Math.Min(MaxBarWidth, System.Math.Abs(Delta.Value) / System.Math.Max(DeltaScale, 1e-9) * MaxBarWidth)) : 0;

        /// <summary>Tells the grid to redraw the delta bar after <see cref="DeltaScale"/> changed.</summary>
        public void RefreshDeltaBar() => OnPropertyChanged(nameof(DeltaBar));

        /// <summary>True when the chosen candidate exists for <paramref name="source"/>.</summary>
        public bool HasCandidate(string source) =>
            source == SourceSurface ? SurfaceZ.HasValue : source == SourceNearest && NearestZ.HasValue;

        /// <summary>Selects <paramref name="source"/> and recalculates New Z, unless that candidate is missing.</summary>
        public void UseSource(string source)
        {
            if (HasCandidate(source)) Source = source;
        }

        /// <summary>Recalculates New Z from the current source and offset (Manual rows keep their typed value).</summary>
        public void Recalculate()
        {
            if (Source == SourceManual) return;
            double? basis = Source == SourceSurface ? SurfaceZ : NearestZ;
            Set(() => NewZ = basis.HasValue ? basis.Value - Offset : null);
            if (!NewZ.HasValue) Include = false;
        }

        partial void OnSourceChanged(string value) => Recalculate();

        partial void OnOffsetChanged(double value) => Recalculate();

        partial void OnNewZChanged(double? value)
        {
            if (_suppress) return;
            Source = SourceManual;
            if (!value.HasValue) Include = false;
        }

        private void Set(System.Action change)
        {
            _suppress = true;
            try { change(); }
            finally { _suppress = false; }
        }
    }
}
