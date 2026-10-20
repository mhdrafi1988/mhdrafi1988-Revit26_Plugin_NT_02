using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.TrackerId.V001.Core.Services
{
    /// <summary>Finds the scope box an element sits in, looking at plan position only.</summary>
    public sealed class ZoneLocator
    {
        private readonly List<ZoneBox> _boxes;

        /// <summary>Prepares the lookup; the smallest box is tried first, so nested boxes win over large ones.</summary>
        public ZoneLocator(IEnumerable<ZoneBox> boxes)
        {
            _boxes = (boxes ?? Enumerable.Empty<ZoneBox>())
                .Where(b => b != null && !string.IsNullOrWhiteSpace(b.Name))
                .OrderBy(b => b.Area)
                .ToList();
        }

        /// <summary>Number of scope boxes taking part.</summary>
        public int Count => _boxes.Count;

        /// <summary>Name of the smallest scope box containing (x, y), or an empty string.</summary>
        public string Find(double x, double y) => _boxes.FirstOrDefault(b => b.Contains(x, y))?.Name ?? string.Empty;
    }
}
