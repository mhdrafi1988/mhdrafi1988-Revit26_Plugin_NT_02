namespace Revit26_Plugin_Revised.EpsSteppedInsulationLayout.V001.Revit
{
    /// <summary>Result of the insulation placement operation.</summary>
    public sealed class PlacementResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public int RoofsCreated { get; init; }

        public static PlacementResult Ok(int count) =>
            new PlacementResult { Success = true, RoofsCreated = count, Message = $"{count} roof elements created." };

        public static PlacementResult Failed(string message) =>
            new PlacementResult { Success = false, Message = message };
    }
}
