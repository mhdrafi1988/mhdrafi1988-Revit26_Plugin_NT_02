using System;
using System.Collections.Generic;
using System.Linq;
using Revit26_Plugin_Revised.AnnotationOverlapReport.V001.Core;
using Xunit;

namespace AnnotationOverlapReport.Tests
{
    public class OverlapFinderTests
    {
        private static PaperBox Box(long id, double x, double y, double w, double h, string type = "Text Notes") =>
            new PaperBox { Id = id, TypeName = type, Text = "t" + id, X = x, Y = y, Width = w, Height = h };

        [Fact]
        public void Touching_boxes_are_not_overlaps()
        {
            var boxes = new[] { Box(1, 0, 0, 10, 5), Box(2, 10, 0, 10, 5), Box(3, 0, 5, 10, 5), Box(4, 20.000001, 0, 5, 5) };
            Assert.Empty(OverlapFinder.Find(boxes, true));
        }

        [Fact]
        public void Overlap_is_smaller_of_two_depths()
        {
            var pairs = OverlapFinder.Find(new[] { Box(1, 0, 0, 10, 5), Box(2, 8, 3, 10, 5) }, true);
            Assert.Single(pairs);
            Assert.Equal(2.0, pairs[0].OverlapMm, 6);
        }

        [Fact]
        public void Contained_box_reports_positive_depth()
        {
            // Inner 2 x 1 box inside a 20 x 10 box: the overlap region is the inner box itself.
            var pairs = OverlapFinder.Find(new[] { Box(1, 0, 0, 20, 10), Box(2, 5, 4, 2, 1) }, true);
            Assert.Single(pairs);
            Assert.Equal(1.0, pairs[0].OverlapMm, 6);
        }

        [Fact]
        public void Same_type_only_when_acrossTypes_false()
        {
            var boxes = new[]
            {
                Box(1, 0, 0, 10, 5, "Door Tags"), Box(2, 5, 2, 10, 5, "Text Notes"), Box(3, 6, 1, 10, 5, "Door Tags")
            };
            Assert.Equal(3, OverlapFinder.Find(boxes, true).Count);
            var same = OverlapFinder.Find(boxes, false);
            Assert.Single(same);
            Assert.Equal(1, same[0].A.Id);
            Assert.Equal(3, same[0].B.Id);
        }

        [Fact]
        public void Each_pair_once_smaller_id_first()
        {
            var pairs = OverlapFinder.Find(new[] { Box(9, 0, 0, 10, 5), Box(4, 5, 2, 10, 5) }, true);
            Assert.Single(pairs);
            Assert.Equal(4, pairs[0].A.Id);
            Assert.Equal(9, pairs[0].B.Id);
        }

        [Fact]
        public void Results_are_sorted_largest_overlap_first()
        {
            var boxes = new[] { Box(1, 0, 0, 10, 10), Box(2, 9, 0, 10, 10), Box(3, 100, 0, 10, 10), Box(4, 105, 0, 10, 10) };
            var pairs = OverlapFinder.Find(boxes, true);
            Assert.Equal(new[] { 5.0, 1.0 }, pairs.Select(p => p.OverlapMm).ToArray());
        }

        [Fact]
        public void Finds_same_pairs_as_brute_force()
        {
            var rnd = new Random(42);
            var boxes = new List<PaperBox>();
            for (int i = 0; i < 2000; i++)
                boxes.Add(Box(i + 1, rnd.NextDouble() * 800, rnd.NextDouble() * 500, 2 + rnd.NextDouble() * 20, 2 + rnd.NextDouble() * 6));

            var expected = new HashSet<(long, long)>();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    double dx = Math.Min(boxes[i].Right, boxes[j].Right) - Math.Max(boxes[i].X, boxes[j].X);
                    double dy = Math.Min(boxes[i].Top, boxes[j].Top) - Math.Max(boxes[i].Y, boxes[j].Y);
                    if (Math.Min(dx, dy) >= OverlapFinder.MinOverlapMm)
                        expected.Add((boxes[i].Id, boxes[j].Id));
                }

            var actual = new HashSet<(long, long)>(OverlapFinder.Find(boxes, true).Select(p => (p.A.Id, p.B.Id)));
            Assert.True(expected.Count > 100);
            Assert.True(expected.SetEquals(actual));
        }

        [Fact]
        public void Null_or_empty_input_gives_no_pairs()
        {
            Assert.Empty(OverlapFinder.Find(null, true));
            Assert.Empty(OverlapFinder.Find(new PaperBox[0], true));
        }

        [Fact]
        public void ModelMmToPaperMm_divides_by_scale()
        {
            Assert.Equal(1.8, OverlapFinder.ModelMmToPaperMm(180, 100), 9);
            Assert.Equal(180, OverlapFinder.ModelMmToPaperMm(180, 0), 9);
        }
    }
}
