using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.LinkedDetailLineGenerator.VA010.Core.Models
{
    /// <summary>
    /// UI-bindable wrapper around a single RevitLinkInstance found in the host document.
    /// VA007: LinkService.GetLinkedModels only ever returns Loaded/CanBeUpgraded links —
    /// Unloaded/NotFound links are excluded entirely (per the enhancement spec), so
    /// every instance of this class the window ever sees IsLoaded == true.
    /// </summary>
    public partial class LinkedModelItem : ObservableObject
    {
        /// <summary>ElementId.Value of the RevitLinkInstance in the host document.</summary>
        public long LinkInstanceId { get; set; }

        /// <summary>Display name of the link instance (e.g. "Architecture").</summary>
        public string InstanceName { get; set; } = string.Empty;

        /// <summary>File name of the linked document (e.g. "Architecture.rvt").</summary>
        public string DocumentTitle { get; set; } = string.Empty;

        /// <summary>True if the link is currently loaded and its document is readable.</summary>
        public bool IsLoaded { get; set; }

        /// <summary>Raw status from RevitLinkType.GetLinkedFileStatus() — VA007 only
        /// ever surfaces Loaded or CanBeUpgraded (see LinkService), the two statuses
        /// that mean "loaded" while still being distinguishable to the user.</summary>
        public LinkedFileStatus Status { get; set; } = LinkedFileStatus.Loaded;

        /// <summary>Display text for the status chip/column — "Needs Review" reads
        /// clearer than the raw enum name "CanBeUpgraded" for a non-technical user.</summary>
        public string StatusDisplay => Status == LinkedFileStatus.CanBeUpgraded ? "Needs Review" : "Loaded";

        /// <summary>ACC Docs status of the link, drawn as the green radio dot in the links list.</summary>
        public AccLinkState AccState { get; set; } = AccLinkState.NotAcc;

        /// <summary>
        /// Checkbox-bound selection state. Multiple links may be checked simultaneously —
        /// checked links are merged into a single combined category tree in Section 2
        /// (see MainViewModel remarks on merged-tree design).
        /// </summary>
        [ObservableProperty]
        private bool _isSelected;
    }
}
