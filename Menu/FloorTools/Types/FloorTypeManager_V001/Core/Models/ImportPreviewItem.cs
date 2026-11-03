using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorTypeManager.V001.Core.Models
{
    public class ImportPreviewItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string           SheetName        { get; set; }
        public string           TypeName         { get; set; }
        public string           DisplayName      { get; set; }  // may differ from TypeName when auto-renamed
        public int              LayerCount       { get; set; }
        public double           TotalThicknessMm { get; set; }
        public ImportStatus     Status           { get; set; }
        public List<LayerData>  Layers           { get; set; } = new List<LayerData>();
        public string           WrapsAtInserts   { get; set; } = "NoInsertWrap";
        public string           WrapsAtEnds      { get; set; } = "NoWrap";

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }

    public enum ImportStatus { New, Dup, Renamed }

    public class LayerData
    {
        public string MaterialName  { get; set; }
        public double ThicknessMm   { get; set; }
        public string LayerFunction { get; set; }
        public int    Priority      { get; set; } = 1;
        public bool   IsVariable    { get; set; }
    }
}
