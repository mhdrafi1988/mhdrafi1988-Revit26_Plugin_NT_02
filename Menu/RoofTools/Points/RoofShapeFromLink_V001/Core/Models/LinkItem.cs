using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.RoofShapeFromLink.V001.Core.Models
{
    /// <summary>A loaded Revit link instance the user can tick as an elevation source.</summary>
    public partial class LinkItem : ObservableObject
    {
        /// <summary>Link instance element id.</summary>
        public long InstanceId { get; init; }

        /// <summary>Link instance name, as shown in the Project Browser.</summary>
        public string Name { get; init; } = "";

        /// <summary>Number of roofs found in the linked model.</summary>
        public int RoofCount { get; init; }

        /// <summary>True when the link is used as an elevation source.</summary>
        [ObservableProperty] private bool _isSelected;
    }
}
