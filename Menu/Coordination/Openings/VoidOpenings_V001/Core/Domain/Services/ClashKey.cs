using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services
{
    /// <summary>
    /// Stable identity of a clash: the two models, the two elements by UniqueId, and which
    /// penetration of that structural element by that MEP element it is (0 for the first along the
    /// MEP element). It does not use ElementId, so it survives sync, reload and re-linking.
    /// </summary>
    public readonly struct ClashKey : IEquatable<ClashKey>
    {
        /// <summary>Key of the structural model.</summary>
        public string StructuralModelKey { get; }

        /// <summary>UniqueId of the structural element.</summary>
        public string StructuralUniqueId { get; }

        /// <summary>Key of the MEP model.</summary>
        public string MepModelKey { get; }

        /// <summary>UniqueId of the MEP element.</summary>
        public string MepUniqueId { get; }

        /// <summary>Index of this penetration among those of the same two elements, along the MEP element.</summary>
        public int Ordinal { get; }

        /// <summary>Creates a key.</summary>
        public ClashKey(string structuralModelKey, string structuralUniqueId, string mepModelKey, string mepUniqueId, int ordinal)
        {
            StructuralModelKey = structuralModelKey ?? string.Empty;
            StructuralUniqueId = structuralUniqueId ?? string.Empty;
            MepModelKey = mepModelKey ?? string.Empty;
            MepUniqueId = mepUniqueId ?? string.Empty;
            Ordinal = ordinal;
        }

        /// <summary>The key as one string, for storing in the model and in exports.</summary>
        public override string ToString() => string.Join("|",
            StructuralModelKey, StructuralUniqueId, MepModelKey, MepUniqueId, Ordinal.ToString(CultureInfo.InvariantCulture));

        /// <summary>Short display id, <c>CL-</c> plus eight hex digits of a hash of the key. The same key always gives the same id.</summary>
        public string ToClashId()
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(ToString()));
            return "CL-" + Convert.ToHexString(hash, 0, 4);
        }

        /// <inheritdoc />
        public bool Equals(ClashKey other) => string.Equals(ToString(), other.ToString(), StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is ClashKey other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());
    }
}
