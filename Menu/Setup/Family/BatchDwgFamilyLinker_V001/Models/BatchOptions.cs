namespace BatchDwgFamilyLinker.Models
{
    public class BatchOptions
    {
        public string FamilyFolderPath { get; set; }
        public string DwgFolderPath { get; set; }
        public DwgPlacementMode PlacementMode { get; set; }
    }

    public enum DwgPlacementMode
    {
        OriginToOrigin,
        CenterToCenter
    }
}
