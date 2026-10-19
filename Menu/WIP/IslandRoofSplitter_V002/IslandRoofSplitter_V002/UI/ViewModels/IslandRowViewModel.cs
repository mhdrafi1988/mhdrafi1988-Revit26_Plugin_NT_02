// IslandRoofSplitter V002 — one row of the islands grid.
#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin.IslandRoofSplitter.V002.Core.Models;

namespace Revit26_Plugin.IslandRoofSplitter.V002.UI.ViewModels
{
    public partial class IslandRowViewModel : ObservableObject
    {
        public Island Island { get; }

        public IslandRowViewModel(Island island)
        {
            Island = island;
            _status = island.Status;
            _reason = island.Reason;
        }

        public string Name => Island.Name;
        public int VertexCount => Island.VertexCount;
        public int HoleCount => Island.Holes.Count;
        public string ZMinMm => Island.ZMin is double z ? (z * LengthUnits.MmPerFoot).ToString("N0") : "–";
        public string ZMaxMm => Island.ZMax is double z ? (z * LengthUnits.MmPerFoot).ToString("N0") : "–";

        [ObservableProperty] private IslandStatus _status;
        [ObservableProperty] private string _reason;
        [ObservableProperty] private string _elementIdText = "";

        /// <summary>V002: Roof or Floor once created — the fallback is visible per island.</summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(KindText))] private ElementKind _kind = ElementKind.None;

        public string KindText => Kind == ElementKind.None ? "–" : Kind.ToString();

        public void ApplyResult(IslandCommitResult r)
        {
            Island.Status = r.Status;
            Island.Reason = r.Reason;
            Island.CreatedElementId = r.ElementId;
            Status = r.Status;
            Reason = r.Reason;
            Kind = r.Kind;
            ElementIdText = r.ElementId?.ToString() ?? "";
        }
    }
}
