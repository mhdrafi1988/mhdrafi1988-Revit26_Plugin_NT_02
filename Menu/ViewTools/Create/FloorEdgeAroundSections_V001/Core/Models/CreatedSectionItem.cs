using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.FloorEdgeAroundSections.V001
{
    /// <summary>
    /// One section view created by the last Run, shown as a checkbox row in the
    /// "Open created sections" popover so the user can pick which views to open.
    /// </summary>
    public partial class CreatedSectionItem : ObservableObject
    {
        /// <summary>Id of the created section view.</summary>
        public ElementId ViewId { get; }

        /// <summary>Section view name as created in the model.</summary>
        public string Name { get; }

        /// <summary>Display name of the floor the section was cut from ("—" if unknown).</summary>
        public string FloorDisplayName { get; }

        /// <summary>Floor side the section was cut on ("—" if unknown).</summary>
        public string Direction { get; }

        /// <summary>True when the user ticked this row to be opened. Starts unticked.</summary>
        [ObservableProperty]
        private bool isSelected;

        /// <summary>Creates a popover row for one created section view.</summary>
        public CreatedSectionItem(ElementId viewId, string name, string floorDisplayName, string direction)
        {
            ViewId = viewId;
            Name = name;
            FloorDisplayName = floorDisplayName;
            Direction = direction;
        }
    }
}
