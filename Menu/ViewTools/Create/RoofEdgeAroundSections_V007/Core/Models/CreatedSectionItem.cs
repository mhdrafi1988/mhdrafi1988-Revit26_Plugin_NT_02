using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.RoofEdgeAroundSections.V007
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

        /// <summary>Display name of the roof the section was cut from ("—" if unknown).</summary>
        public string RoofDisplayName { get; }

        /// <summary>Roof side the section was cut on ("—" if unknown).</summary>
        public string Direction { get; }

        /// <summary>True when the user ticked this row to be opened. Starts unticked.</summary>
        [ObservableProperty]
        private bool isSelected;

        /// <summary>Creates a popover row for one created section view.</summary>
        public CreatedSectionItem(ElementId viewId, string name, string roofDisplayName, string direction)
        {
            ViewId = viewId;
            Name = name;
            RoofDisplayName = roofDisplayName;
            Direction = direction;
        }
    }
}
