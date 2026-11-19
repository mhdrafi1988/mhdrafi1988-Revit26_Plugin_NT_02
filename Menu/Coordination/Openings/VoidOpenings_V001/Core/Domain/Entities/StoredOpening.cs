namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities
{
    /// <summary>
    /// The record kept on a generated void instance (as JSON in Extensible Storage). It is what the
    /// tool trusts when it runs again: which clash the void answers, its Job ID, and the geometry it
    /// was generated with. Plain properties so it serialises as is; add fields, never rename them.
    /// </summary>
    public sealed class StoredOpening
    {
        /// <summary>Format version of the record.</summary>
        public int Version { get; set; } = 1;

        /// <summary>The clash key as text (see <c>ClashKey.ToString</c>).</summary>
        public string ClashKey { get; set; }

        /// <summary>Display id of the clash.</summary>
        public string ClashId { get; set; }

        /// <summary>Job ID of the opening.</summary>
        public string JobId { get; set; }

        /// <summary>Geometry the void was generated with, in the structural model's coordinates.</summary>
        public string Signature { get; set; }

        /// <summary>Geometry of the void instance as it is in the model now. Read from the instance, never stored.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string CurrentSignature { get; set; }

        /// <summary>UniqueId of the void instance. Read from the instance, never stored.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string InstanceUniqueId { get; set; }

        /// <summary>"Cut" or "Generated".</summary>
        public string Status { get; set; }

        /// <summary>Key of the structural model.</summary>
        public string StructuralModelKey { get; set; }

        /// <summary>Name of the structural model.</summary>
        public string StructuralModelName { get; set; }

        /// <summary>UniqueId of the structural element.</summary>
        public string StructuralUniqueId { get; set; }

        /// <summary>Element id of the structural element when the void was generated.</summary>
        public long StructuralElementId { get; set; }

        /// <summary>Structural category (enum name).</summary>
        public string StructuralCategory { get; set; }

        /// <summary>Key of the MEP model.</summary>
        public string MepModelKey { get; set; }

        /// <summary>Name of the MEP model.</summary>
        public string MepModelName { get; set; }

        /// <summary>UniqueId of the MEP element.</summary>
        public string MepUniqueId { get; set; }

        /// <summary>Element id of the MEP element when the void was generated.</summary>
        public long MepElementId { get; set; }

        /// <summary>MEP category (enum name).</summary>
        public string MepCategory { get; set; }

        /// <summary>"Circular" or "Rectangular".</summary>
        public string Shape { get; set; }

        /// <summary>Opening width in millimetres (rectangular).</summary>
        public double WidthMm { get; set; }

        /// <summary>Opening height in millimetres (rectangular).</summary>
        public double HeightMm { get; set; }

        /// <summary>Opening diameter in millimetres (circular).</summary>
        public double DiameterMm { get; set; }

        /// <summary>Void depth in millimetres.</summary>
        public double DepthMm { get; set; }

        /// <summary>Clearance that was applied, in millimetres.</summary>
        public double ClearanceMm { get; set; }

        /// <summary>When the void was generated (UTC, ISO 8601).</summary>
        public string CreatedUtc { get; set; }
    }
}
