using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern;

namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Revit
{
    /// <summary>
    /// Writes the pattern as detail lines (+ optional text notes and detail groups) in the view
    /// where the floor was picked. ONE transaction — any failure rolls back everything.
    /// Must run in a valid API context (ExternalEvent handler).
    /// </summary>
    public static class DetailPatternWriter
    {
        public const string TransactionName = "Tapered EPS 2D Pattern";

        public static PatternWriteResult Write(PatternWriteRequest request)
        {
            FloorContext ctx = request.Floor;
            Document doc = ctx.Document;

            if (!doc.IsValidObject)
            {
                return PatternWriteResult.Failed("The document was closed.");
            }

            if (doc.GetElement(ctx.ViewId) is not ViewPlan view || doc.GetElement(ctx.FloorId) is not Floor)
            {
                return PatternWriteResult.Failed("The floor or the view no longer exists. Close the tool and run it again.");
            }

            if (!request.Pattern.IsValid)
            {
                return PatternWriteResult.Failed("The pattern is not valid: " + string.Join(" ", request.Pattern.Errors));
            }

            double z = ctx.LevelElevationFeet;
            double shortTol = Math.Max(ctx.ShortCurveToleranceFeet, doc.Application.ShortCurveTolerance);
            var created = new List<ElementId>();
            var buckets = new Dictionary<string, List<ElementId>>();
            var perLayer = new Dictionary<PatternLayer, int>();
            var warnings = new List<string>();
            int skipped = 0, textNotes = 0, replaced = 0, regions = 0, fillTypesCreated = 0, regionsSkipped = 0;
            var groupNames = new List<string>();
            HashSet<ElementId>? allowedStyles = null;

            var swallower = new WarningSwallower();
            using (var t = new Transaction(doc, TransactionName))
            {
                FailureHandlingOptions failureOptions = t.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(swallower);
                failureOptions.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(failureOptions);
                t.Start();
                try
                {
                    if (request.Grouping != GroupingMode.NoGroup && request.ReplacePrevious)
                    {
                        replaced = DetailGroupManager.DeleteExisting(doc, ctx.GroupName);
                    }

                    // Hatches first, so the detail lines are drawn on top of them.
                    if (request.Pattern.Hatches.Count > 0)
                    {
                        IReadOnlyList<ElementId> slotTypes = request.HatchSource == HatchTypeSource.CreateToolTypes
                            ? FillTypeProvider.EnsureToolTypes(doc, request.Pattern.HatchSlotCount, out fillTypesCreated)
                            : request.PickedHatchTypeIds.Where(id => id != null && id != ElementId.InvalidElementId).ToList();
                        ElementId? invisible = LineStyleProvider.FindInvisible(doc);

                        if (slotTypes.Count == 0)
                        {
                            warnings.Add("Hatching skipped: no filled region type available.");
                        }
                        else
                        {
                            foreach (HatchRegion h in request.Pattern.Hatches)
                            {
                                CurveLoop? loop = ToCurveLoop(h.Polygon, z, shortTol);
                                if (loop == null)
                                {
                                    regionsSkipped++;
                                    continue;
                                }

                                FilledRegion region = FilledRegion.Create(doc, slotTypes[h.Slot % slotTypes.Count], view.Id, new List<CurveLoop> { loop });
                                if (invisible != null && FilledRegion.IsValidLineStyleIdForFilledRegion(doc, invisible))
                                {
                                    region.SetLineStyleId(invisible);
                                }

                                created.Add(region.Id);
                                Bucket(buckets, request.Grouping == GroupingMode.GroupPerSide && h.EdgeIndex.HasValue
                                    ? $"E{h.EdgeIndex.Value + 1}" : "Hatch").Add(region.Id);
                                regions++;
                            }
                        }
                    }

                    foreach (PatternSegment seg in request.Pattern.Segments)
                    {
                        XYZ a = UnitConverter.ToModel(seg.Segment.Start, z);
                        XYZ b = UnitConverter.ToModel(seg.Segment.End, z);
                        if (a.DistanceTo(b) < shortTol)
                        {
                            skipped++;
                            continue;
                        }

                        DetailCurve curve = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                        allowedStyles ??= new HashSet<ElementId>(curve.GetLineStyleIds());

                        if (request.LineStyles.TryGetValue(seg.Layer, out ElementId? styleId) &&
                            styleId != null && allowedStyles.Contains(styleId) &&
                            doc.GetElement(styleId) is GraphicsStyle style)
                        {
                            curve.LineStyle = style;
                        }
                        else if (!warnings.Contains(seg.Layer.ToString()))
                        {
                            warnings.Add(seg.Layer.ToString());
                        }

                        created.Add(curve.Id);
                        string key = request.Grouping == GroupingMode.GroupPerSide && seg.EdgeIndex.HasValue
                            ? $"E{seg.EdgeIndex.Value + 1}"
                            : seg.Layer == PatternLayer.Grid && seg.EdgeIndex.HasValue
                                ? $"Grid_E{seg.EdgeIndex.Value + 1}"
                                : LayerSuffix(seg.Layer);
                        Bucket(buckets, key).Add(curve.Id);
                        perLayer[seg.Layer] = perLayer.TryGetValue(seg.Layer, out int n) ? n + 1 : 1;
                    }

                    if (request.Pattern.Labels.Count > 0 && request.TextNoteTypeId != null &&
                        request.TextNoteTypeId != ElementId.InvalidElementId)
                    {
                        var options = new TextNoteOptions(request.TextNoteTypeId)
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Center,
                            VerticalAlignment = VerticalTextAlignment.Middle
                        };

                        foreach (PatternLabel label in request.Pattern.Labels)
                        {
                            TextNote note = TextNote.Create(doc, view.Id, UnitConverter.ToModel(label.Position, z), label.Text, options);
                            created.Add(note.Id);
                            Bucket(buckets, request.Grouping == GroupingMode.GroupPerSide && label.EdgeIndex.HasValue
                                ? $"E{label.EdgeIndex.Value + 1}" : "Labels").Add(note.Id);
                            textNotes++;
                        }
                    }

                    if (created.Count == 0)
                    {
                        t.RollBack();
                        return PatternWriteResult.Failed("Nothing to create — every segment was shorter than Revit's short-curve tolerance.");
                    }

                    if (request.Grouping == GroupingMode.SingleGroup)
                    {
                        groupNames.Add(DetailGroupManager.Create(doc, created, ctx.GroupName).GroupType.Name);
                    }
                    else if (request.Grouping == GroupingMode.GroupPerLayer || request.Grouping == GroupingMode.GroupPerSide)
                    {
                        foreach (KeyValuePair<string, List<ElementId>> bucket in buckets)
                        {
                            groupNames.Add(DetailGroupManager.Create(doc, bucket.Value, $"{ctx.GroupName}_{bucket.Key}").GroupType.Name);
                        }
                    }

                    if (t.Commit() != TransactionStatus.Committed)
                    {
                        return PatternWriteResult.Failed("Revit did not commit the transaction; nothing was created.");
                    }
                }
                catch (Exception ex)
                {
                    if (t.GetStatus() == TransactionStatus.Started)
                    {
                        t.RollBack();
                    }

                    return PatternWriteResult.Failed("Creation failed and was rolled back: " + ex.Message);
                }
            }

            return new PatternWriteResult
            {
                Success = true,
                LinesCreated = perLayer.Values.Sum(),
                TextNotesCreated = textNotes,
                FilledRegionsCreated = regions,
                FillTypesCreated = fillTypesCreated,
                SkippedShortSegments = skipped,
                ReplacedGroups = replaced,
                GroupNames = groupNames,
                LinesPerLayer = perLayer,
                Warnings = warnings
                    .Select(l => $"Line style for layer {l} is not usable on detail lines; Revit's default style was kept.")
                    .Concat(regionsSkipped > 0 ? new[] { $"{regionsSkipped} hatch region(s) skipped: an edge was shorter than Revit's short-curve tolerance." } : Array.Empty<string>())
                    .Concat(fillTypesCreated > 0 ? new[] { $"{fillTypesCreated} filled region type(s) {FillTypeProvider.ToolTypePrefix}nn created." } : Array.Empty<string>())
                    .Concat(swallower.Messages.Select(m => "Revit warning (suppressed): " + m))
                    .ToList(),
                Message = $"{perLayer.Values.Sum()} lines created" +
                          (textNotes > 0 ? $", {textNotes} labels" : string.Empty) +
                          (regions > 0 ? $", {regions} hatches" : string.Empty) +
                          (groupNames.Count == 1 ? $" in group {groupNames[0]}" :
                           groupNames.Count > 1 ? $" in {groupNames.Count} groups ({string.Join(", ", groupNames)})" :
                           " as loose detail lines") + "."
            };
        }

        /// <summary>Polygon (m) → closed CurveLoop (ft) at elevation z; drops vertices closer than the short-curve tolerance.</summary>
        private static CurveLoop? ToCurveLoop(Polygon2D polygon, double z, double shortTol)
        {
            var points = new List<XYZ>();
            foreach (Vec2 v in polygon.Vertices)
            {
                XYZ p = UnitConverter.ToModel(v, z);
                if (points.Count == 0 || points[points.Count - 1].DistanceTo(p) >= shortTol)
                {
                    points.Add(p);
                }
            }

            while (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) < shortTol)
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count < 3)
            {
                return null;
            }

            var loop = new CurveLoop();
            for (int i = 0; i < points.Count; i++)
            {
                loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Count]));
            }

            return loop;
        }

        private static string LayerSuffix(PatternLayer layer) => layer switch
        {
            PatternLayer.Grid => "Grid",
            PatternLayer.HipRidge => "HipRidge",
            PatternLayer.Contour => "Contours",
            _ => layer.ToString()
        };

        private static List<ElementId> Bucket(Dictionary<string, List<ElementId>> buckets, string key)
        {
            if (!buckets.TryGetValue(key, out List<ElementId>? list))
            {
                list = new List<ElementId>();
                buckets[key] = list;
            }

            return list;
        }
    }
}
