using System.Collections.Generic;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Models
{
    /// <summary>What the Model segment needs to know about the open Revit file.</summary>
    public sealed class ModelIdentity
    {
        /// <summary>Document title (file name without .rvt), e.g. "NT_02_ARC_Central".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Values of the Project Information parameters the format asks for, keyed by parameter name.
        /// A missing key means the parameter does not exist.
        /// </summary>
        public Dictionary<string, string> ProjectParameterValues { get; set; } = new();
    }
}
