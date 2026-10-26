using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Revit26_Plugin_Revised.DetailLineManager.V002.Core.Services
{
    /// <summary>
    /// Counts which line styles are used, across the whole project: every <see cref="CurveElement"/>
    /// (model, detail, symbolic and room/area separation lines), every <see cref="ModelCurve"/> in the
    /// Lines category, and sketch lines (filled-region and other sketch boundaries), including lines
    /// in groups. Runs in steps so a long count can show progress and be cancelled between steps.
    /// Not counted, because the Revit API does not expose it: edges restyled with the Linework tool.
    /// Call only in the Revit API context.
    /// </summary>
    public sealed class UsageCounter
    {
        private readonly HashSet<ElementId> _seen = new();
        private readonly List<ElementId> _curves = new();
        private readonly List<ElementId> _sketches = new();
        private readonly List<ElementId> _regions = new();
        private bool _prepared;
        private int _index;

        /// <summary>Creates a counter for <paramref name="doc"/>.</summary>
        public UsageCounter(Document doc) => Document = doc ?? throw new ArgumentNullException(nameof(doc));

        /// <summary>Document being counted.</summary>
        public Document Document { get; }

        /// <summary>Line style subcategory id → number of curve elements using it (so far).</summary>
        public Dictionary<ElementId, int> Usage { get; } = new();

        /// <summary>Items processed so far.</summary>
        public int Done => _index;

        /// <summary>Items to process (known after the first step).</summary>
        public int Total => _curves.Count + _sketches.Count + _regions.Count;

        /// <summary>
        /// Processes elements for up to <paramref name="budget"/>. Returns true when the count is complete.
        /// </summary>
        public bool Step(TimeSpan budget)
        {
            var doc = Document;
            if (!_prepared)
            {
                var ids = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).ToElementIds());
                // ModelCurves are CurveElements, but collect the Lines category as well in case the
                // class filter leaves some out.
                ids.UnionWith(new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Lines).WhereElementIsNotElementType().ToElementIds());
                _curves.AddRange(ids);
                _sketches.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Sketch)).ToElementIds());
                _regions.AddRange(new FilteredElementCollector(doc).OfClass(typeof(FilledRegion)).ToElementIds());
                _prepared = true;
                return Total == 0;
            }

            var clock = Stopwatch.StartNew();
            var curveFilter = new ElementClassFilter(typeof(CurveElement));
            while (_index < Total)
            {
                int i = _index++;
                if (i < _curves.Count)
                {
                    Add(doc.GetElement(_curves[i]));
                }
                else if (i < _curves.Count + _sketches.Count)
                {
                    if (doc.GetElement(_sketches[i - _curves.Count]) is Sketch sketch)
                    {
                        try { foreach (var id in sketch.GetAllElements()) Add(doc.GetElement(id)); }
                        catch (Autodesk.Revit.Exceptions.ApplicationException) { /* unreadable sketch */ }
                    }
                }
                else if (doc.GetElement(_regions[i - _curves.Count - _sketches.Count]) is Element region)
                {
                    try { foreach (var id in region.GetDependentElements(curveFilter)) Add(doc.GetElement(id)); }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { /* unreadable region */ }
                }

                if ((i & 127) == 0 && clock.Elapsed > budget) break;
            }
            return _index >= Total;
        }

        private void Add(Element element)
        {
            if (element is not CurveElement curve || !_seen.Add(curve.Id)) return;
            ElementId catId;
            try { catId = (curve.LineStyle as GraphicsStyle)?.GraphicsStyleCategory?.Id; }
            catch (Autodesk.Revit.Exceptions.ApplicationException) { return; }
            if (catId == null) return;
            Usage[catId] = Usage.TryGetValue(catId, out int n) ? n + 1 : 1;
        }
    }
}
