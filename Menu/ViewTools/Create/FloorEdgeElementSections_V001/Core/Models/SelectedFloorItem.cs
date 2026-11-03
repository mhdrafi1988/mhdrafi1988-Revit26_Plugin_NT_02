using System;
using Autodesk.Revit.DB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// UI-bindable wrapper around a single floor picked into the "1. Floors" list via
    /// the PickFloors command. Each row carries its own RemoveCommand, invoking
    /// RemoveAction (wired by the owning ViewModel) so this class doesn't need a
    /// back-reference to the ViewModel itself.
    /// </summary>
    public partial class SelectedFloorItem : ObservableObject
    {
        public ElementId FloorId { get; }
        public Floor FloorElement { get; }
        public string DisplayName { get; }

        /// <summary>Invoked by the RemoveCommand; wired by the owning ViewModel.</summary>
        public Action<SelectedFloorItem> RemoveAction { get; set; }

        public SelectedFloorItem(Floor floor, string displayName)
        {
            FloorElement = floor;
            FloorId = floor.Id;
            DisplayName = displayName;
        }

        [RelayCommand]
        private void Remove() => RemoveAction?.Invoke(this);
    }
}
