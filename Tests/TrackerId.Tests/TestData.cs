using Revit26_Plugin_Revised.TrackerId.V001.Core.Models;
using Revit26_Plugin_Revised.TrackerId.V001.Core.Services;
using System.Collections.Generic;
using System.Linq;

namespace TrackerId.Tests
{
    /// <summary>Small model shared by the tests: grids A–D × 1–5 at 8 m, one door type, model ARC.</summary>
    internal static class TestData
    {
        public static double Ft(double metres) => metres / 0.3048;

        public static GridLocator Grids()
        {
            var grids = new List<GridLine2D>();
            string[] letters = { "A", "B", "C", "D" };
            for (int i = 0; i < letters.Length; i++)
                grids.Add(new GridLine2D { Name = letters[i], X1 = Ft(i * 8), Y1 = Ft(-10), X2 = Ft(i * 8), Y2 = Ft(50) });
            for (int i = 1; i <= 5; i++)
                grids.Add(new GridLine2D { Name = i.ToString(), X1 = Ft(-10), Y1 = Ft((i - 1) * 8), X2 = Ft(50), Y2 = Ft((i - 1) * 8) });
            return new GridLocator(grids);
        }

        public static readonly ModelIdentity Model = new() { Title = "NT_02_ARC_Central_jdoe" };

        public static ElementSnapshot Door(long id, string trackerId = "", string sourceGuid = "", string zone = "ZA",
            double x = 1, double y = 1, string uniqueId = null) => new()
        {
            ElementId = id,
            UniqueId = uniqueId ?? "u" + id,
            CategoryId = -2000023,
            CategoryName = "Doors",
            TypeMark = "D09",
            TypeName = "0900 x 2100",
            LevelName = "Level 01",
            HasCenter = true,
            CenterX = Ft(x),
            CenterY = Ft(y),
            ParameterValues = new Dictionary<string, string> { { "Zone", zone }, { "Workset", "ARC" } },
            TrackerId = trackerId,
            SourceGuid = sourceGuid,
            Model = Model
        };

        public static TrackerFormat Format() => new() { Segments = SegmentCatalog.DefaultSegments() };

        public static List<TrackerIdHolder> Holders(IEnumerable<ElementSnapshot> elements) =>
            elements.Where(e => e.TrackerId.Length > 0)
                    .Select(e => new TrackerIdHolder(e.ElementId, e.UniqueId, e.TrackerId, e.SourceGuid))
                    .ToList();
    }
}
