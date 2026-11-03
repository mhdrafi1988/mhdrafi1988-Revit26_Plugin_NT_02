using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Autodesk.Revit.DB;

namespace Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Models
{
    public partial class FloorTypeItem : ObservableObject
    {
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool isExpanded;

        public ElementId       TypeId           { get; set; }
        public string          TypeName         { get; set; }
        public string          TypeMark         { get; set; }
        public string          Function         { get; set; }
        public int             LayerCount       { get; set; }
        public double          TotalThicknessMm { get; set; }
        public List<LayerData> Layers           { get; set; } = new List<LayerData>();
    }
}
