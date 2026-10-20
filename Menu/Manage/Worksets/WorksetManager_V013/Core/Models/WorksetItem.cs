using CommunityToolkit.Mvvm.ComponentModel;

namespace Revit26_Plugin_Revised.WorksetManager.V013.Core.Models
{
    public enum WorksetGridCategory
    {
        /// <summary>Grid 1 — proposed workset exists and all instances are fully assigned.</summary>
        AlreadyAssigned,

        /// <summary>Grid 2 — link has instances but proposed workset does not exist yet.</summary>
        NeedsWorkset,

        /// <summary>Grid 3 — no instances found (unloaded / not placed).</summary>
        NoInstances
    }

    public partial class WorksetItem : ObservableObject
    {
        [ObservableProperty] private int    serialNumber;
        [ObservableProperty] private string linkName;
        [ObservableProperty] private string currentWorksetName  = "None";
        [ObservableProperty] private bool   isMixedWorkset;
        [ObservableProperty] private string proposedWorksetName;
        [ObservableProperty] private string proposedWorksetTooltip;
        [ObservableProperty] private bool   isSelected;
        [ObservableProperty] private bool   hasInstances        = true;
        [ObservableProperty] private bool   isExactMatchAssigned;
        [ObservableProperty] private bool   isExistingWorkset;
        [ObservableProperty] private string existingWorksetName;
        [ObservableProperty] private int    instanceCount;
        [ObservableProperty] private WorksetGridCategory gridCategory;
    }
}
