using CommunityToolkit.Mvvm.ComponentModel;
using Revit26_Plugin_Revised.Shared.Services;

namespace Revit26_Plugin_Revised.RoofEdgeElementSections.V004
{
    /// <summary>
    /// UI-bindable wrapper around a single RevitLinkInstance found in the host document.
    /// Copied from LinkedDetailLineGenerator_VA003's LinkedModelItem.
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

        /// <summary>ACC Docs status of the link, drawn as the green radio dot in the links list.</summary>
        public AccLinkState AccState { get; set; } = AccLinkState.NotAcc;

        /// <summary>
        /// Checkbox-bound selection state. Multiple links may be checked simultaneously —
        /// checked links are merged into a single combined Category→Family→Type tree.
        /// </summary>
        [ObservableProperty]
        private bool _isSelected;
    }
}
