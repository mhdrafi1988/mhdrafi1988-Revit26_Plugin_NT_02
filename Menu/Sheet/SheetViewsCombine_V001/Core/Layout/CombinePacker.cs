using System;
using System.Collections.Generic;
using System.Linq;

namespace Revit26_Plugin_Revised.SheetViewsCombine.V001.Core.Layout
{
    /// <summary>How <see cref="CombinePacker"/> fills the sheet.</summary>
    public enum PackStrategy
    {
        /// <summary>Reading order: left to right in rows, rows top to bottom, in list order.</summary>
        Rows = 0,

        /// <summary>Largest views first, each in the highest, then leftmost, free spot.</summary>
        MaxFill = 1
    }

    /// <summary>One view to pack: its footprint (box plus title) in feet.</summary>
    public sealed class PackItem
    {
        /// <summary>Creates an item.</summary>
        public PackItem(long key, double width, double height)
        {
            Key = key;
            Width = width;
            Height = height;
        }

        /// <summary>Caller's identity for the item.</summary>
        public long Key { get; }

        /// <summary>Footprint width (feet).</summary>
        public double Width { get; }

        /// <summary>Footprint height (feet).</summary>
        public double Height { get; }
    }

    /// <summary>Outcome of <see cref="CombinePacker.Pack"/>.</summary>
    public sealed class PackResult
    {
        /// <summary>Where each placed item's footprint goes, in placement order.</summary>
        public List<KeyValuePair<long, SheetRect>> Placed { get; } = new();

        /// <summary>Items that found no room, in list order.</summary>
        public List<long> Unplaced { get; } = new();

        /// <summary>The placed rectangle for <paramref name="key"/>, or null.</summary>
        public SheetRect? Find(long key)
        {
            foreach (var p in Placed)
                if (p.Key == key)
                    return p.Value;
            return null;
        }
    }

    /// <summary>
    /// Packs view footprints into a sheet's usable area around fixed obstacles (views that stay
    /// in place, legend copies, schedules). Works the same way as Sheet Placer's row and max-fill
    /// strategies, with obstacle support added. Revit-free and deterministic.
    /// </summary>
    public static class CombinePacker
    {
        private const double Tol = 1e-7;
        private const int MaxSteps = 200000;

        /// <summary>
        /// Packs <paramref name="items"/> into <paramref name="region"/>. Placed footprints keep at least
        /// <paramref name="hGap"/> / <paramref name="vGap"/> from each other and from <paramref name="obstacles"/>.
        /// </summary>
        public static PackResult Pack(SheetRect region, IReadOnlyList<SheetRect> obstacles, IReadOnlyList<PackItem> items,
            double hGap, double vGap, PackStrategy strategy)
        {
            var result = new PackResult();
            items ??= Array.Empty<PackItem>();
            hGap = Math.Max(0, double.IsFinite(hGap) ? hGap : 0);
            vGap = Math.Max(0, double.IsFinite(vGap) ? vGap : 0);
            if (!region.IsValid)
            {
                result.Unplaced.AddRange(items.Select(i => i.Key));
                return result;
            }

            var occupied = (obstacles ?? Array.Empty<SheetRect>()).Where(o => o.IsValid).ToList();
            if (strategy == PackStrategy.MaxFill)
                PackMaxFill(region, occupied, items, hGap, vGap, result);
            else
                PackRows(region, occupied, items, hGap, vGap, result);
            return result;
        }

        private static bool Fits(PackItem item, SheetRect region)
            => item.Width > 0 && item.Height > 0 && double.IsFinite(item.Width) && double.IsFinite(item.Height)
               && item.Width <= region.Width + Tol && item.Height <= region.Height + Tol;

        private static SheetRect? FirstBlocker(SheetRect candidate, List<SheetRect> occupied, double hGap, double vGap)
        {
            SheetRect? best = null;
            foreach (var r in occupied)
            {
                if (!candidate.Overlaps(r.Grow(hGap, vGap), Tol))
                    continue;
                // The blocker that reaches furthest right lets the scan skip the most.
                if (best == null || r.MaxX > best.Value.MaxX)
                    best = r;
            }
            return best;
        }

        private static void PackRows(SheetRect region, List<SheetRect> occupied, IReadOnlyList<PackItem> items,
            double hGap, double vGap, PackResult result)
        {
            double rowTop = region.MaxY;
            double x = region.MinX;
            double rowBottom = rowTop;
            bool rowHasItems = false;
            int steps = 0;

            foreach (var item in items)
            {
                if (!Fits(item, region))
                {
                    result.Unplaced.Add(item.Key);
                    continue;
                }

                // Work on copies so an item that does not fit leaves the row as it was for the next one.
                double tTop = rowTop, tX = x, tBottom = rowBottom;
                bool tHasItems = rowHasItems;
                bool placed = false;

                while (++steps < MaxSteps)
                {
                    if (tTop - item.Height < region.MinY - Tol)
                        break; // no row left on this sheet

                    if (tX + item.Width > region.MaxX + Tol)
                    {
                        // Next row.
                        double next;
                        if (tHasItems)
                        {
                            next = tBottom - vGap;
                        }
                        else
                        {
                            // An empty row blocked all the way across: drop below the highest blocker in the band.
                            double band = tTop - item.Height;
                            next = double.NegativeInfinity;
                            foreach (var r in occupied)
                            {
                                double below = r.MinY - vGap;
                                if (below < tTop - Tol && r.MaxY + vGap > band + Tol && below > next)
                                    next = below;
                            }
                            if (double.IsNegativeInfinity(next))
                                break;
                        }
                        tTop = Math.Min(next, region.MaxY);
                        tX = region.MinX;
                        tBottom = tTop;
                        tHasItems = false;
                        continue;
                    }

                    var candidate = SheetRect.FromTopLeft(tX, tTop, item.Width, item.Height);
                    var blocker = FirstBlocker(candidate, occupied, hGap, vGap);
                    if (blocker == null)
                    {
                        occupied.Add(candidate);
                        result.Placed.Add(new KeyValuePair<long, SheetRect>(item.Key, candidate));
                        tX = candidate.MaxX + hGap;
                        tBottom = tHasItems ? Math.Min(tBottom, candidate.MinY) : candidate.MinY;
                        tHasItems = true;
                        placed = true;
                        break;
                    }

                    double skipTo = blocker.Value.MaxX + hGap;
                    tX = skipTo > tX + Tol ? skipTo : tX + Math.Max(Tol * 10, item.Width / 1000);
                }

                if (placed)
                {
                    rowTop = tTop;
                    x = tX;
                    rowBottom = tBottom;
                    rowHasItems = tHasItems;
                }
                else
                {
                    result.Unplaced.Add(item.Key);
                }
            }
        }

        private static void PackMaxFill(SheetRect region, List<SheetRect> occupied, IReadOnlyList<PackItem> items,
            double hGap, double vGap, PackResult result)
        {
            var order = items.Select((item, index) => (item, index))
                .OrderByDescending(p => p.item.Width * p.item.Height)
                .ThenBy(p => p.index)
                .Select(p => p.item)
                .ToList();

            foreach (var item in order)
            {
                if (!Fits(item, region))
                {
                    result.Unplaced.Add(item.Key);
                    continue;
                }

                var xs = new List<double> { region.MinX };
                var tops = new List<double> { region.MaxY };
                foreach (var r in occupied)
                {
                    xs.Add(r.MaxX + hGap);
                    xs.Add(r.MinX);
                    tops.Add(r.MinY - vGap);
                    tops.Add(r.MaxY);
                }

                SheetRect? best = null;
                foreach (double top in tops.Where(t => t <= region.MaxY + Tol).Distinct().OrderByDescending(t => t))
                {
                    if (best != null && top < best.Value.MaxY - Tol)
                        break; // candidates are sorted top-down: the first row that has a spot wins
                    foreach (double cx in xs.Where(v => v >= region.MinX - Tol).Distinct().OrderBy(v => v))
                    {
                        var candidate = SheetRect.FromTopLeft(cx, Math.Min(top, region.MaxY), item.Width, item.Height);
                        if (!region.Contains(candidate, Tol))
                            continue;
                        if (FirstBlocker(candidate, occupied, hGap, vGap) != null)
                            continue;
                        best = candidate;
                        break;
                    }
                }

                if (best is SheetRect spot)
                {
                    occupied.Add(spot);
                    result.Placed.Add(new KeyValuePair<long, SheetRect>(item.Key, spot));
                }
                else
                {
                    result.Unplaced.Add(item.Key);
                }
            }

            // Report unplaced items in list order, whatever order they were tried in.
            var listOrder = items.Select((item, index) => (item.Key, index)).ToDictionary(p => p.Key, p => p.index);
            result.Unplaced.Sort((a, b) => listOrder[a].CompareTo(listOrder[b]));
        }
    }
}
