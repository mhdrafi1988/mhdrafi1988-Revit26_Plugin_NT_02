using System.Collections.Generic;

namespace Revit26_Plugin_Revised.FloorEdgeElementSections.V001
{
    /// <summary>
    /// The set of tokens available for building a section view name.
    /// Adapted from FloorEdgeAroundSections_V004's NamingToken, with a new
    /// "Category" token — the category of the matched linked-element cluster
    /// this section was built from.
    /// </summary>
    public enum NamingTokenType
    {
        /// <summary>Floor's "Zone" parameter value (instance, falling back to type). Contributes nothing if blank.</summary>
        Zone,

        /// <summary>Name of the Level the floor is associated with.</summary>
        Level,

        /// <summary>Floor's display name (FloorDisplayName — existing Name param, or "Floor_{ElementId}" fallback).</summary>
        Name,

        /// <summary>Combined static text "Line of" + edge direction, e.g. "Line of North".</summary>
        LineOfDirection,

        /// <summary>Category display name of the matched linked-element cluster (e.g. "Walls").</summary>
        Category,

        /// <summary>Floor's Area parameter value, rounded, with unit suffix.</summary>
        Area,

        /// <summary>Auto-increment sequence number. Always included when enabled (not duplicate-only).</summary>
        Number
    }

    /// <summary>
    /// One token in the section-naming pattern: which token, whether it's included,
    /// and its position among the other enabled tokens. Persisted as part of
    /// <see cref="FloorEdgeElementSectionsSettings"/>.
    /// </summary>
    public class NamingToken
    {
        public NamingTokenType Type { get; set; }
        public bool IsEnabled { get; set; }

        /// <summary>Position among enabled tokens (lower = earlier in the built name).</summary>
        public int Order { get; set; }

        /// <summary>
        /// Default token set and order: Zone, Level, Name, Line of Direction, Category,
        /// Number enabled; Area disabled.
        /// </summary>
        public static List<NamingToken> Defaults() => new()
        {
            new NamingToken { Type = NamingTokenType.Zone,            IsEnabled = true,  Order = 1 },
            new NamingToken { Type = NamingTokenType.Level,           IsEnabled = true,  Order = 2 },
            new NamingToken { Type = NamingTokenType.Name,            IsEnabled = true,  Order = 3 },
            new NamingToken { Type = NamingTokenType.LineOfDirection, IsEnabled = true,  Order = 4 },
            new NamingToken { Type = NamingTokenType.Category,        IsEnabled = true,  Order = 5 },
            new NamingToken { Type = NamingTokenType.Area,            IsEnabled = false, Order = 6 },
            new NamingToken { Type = NamingTokenType.Number,          IsEnabled = true,  Order = 7 },
        };
    }
}
