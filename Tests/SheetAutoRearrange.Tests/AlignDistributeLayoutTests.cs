using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Models;
using Revit26_Plugin_Revised.SheetAutoRearrange.V030.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SheetAutoRearrange.Tests
{
    /// <summary>Align &amp; Distribute layout (V029). Area is 600 x 400, origin bottom-left, Y up; units are mm.</summary>
    public class AlignDistributeLayoutTests
    {
        private const double W = 600, H = 400, Gap = 20, Tol = 50;

        /// <summary>Box from its left / top corner, the way a packer places it.</summary>
        private static LayoutBox Box(int key, double left, double top, double w, double h) =>
            new LayoutBox(key, left + w / 2.0, top - h / 2.0, w, h);

        /// <summary>Two packed rows of three mixed-size views, top-left packed.</summary>
        private static List<LayoutBox> TwoRows() => new List<LayoutBox>
        {
            Box(0, 0, 400, 100, 120), Box(1, 120, 400, 80, 60), Box(2, 220, 400, 140, 90),
            Box(3, 0, 260, 60, 80), Box(4, 80, 260, 150, 100), Box(5, 250, 260, 90, 50),
        };

        private static AlignDistributeResult Run(List<LayoutBox> boxes, AlignDistributeSettings s,
            BlockAlignmentH h = BlockAlignmentH.Left, BlockAlignmentV v = BlockAlignmentV.Top, double width = W, double height = H) =>
            AlignDistributeLayout.Arrange(boxes, 0, 0, width, height, s, Gap, Gap, Tol, Tol, h, v);

        private static double Left(AlignDistributeResult r, LayoutBox b) => r.Centers[b.Key].X - b.Width / 2.0;
        private static double Right(AlignDistributeResult r, LayoutBox b) => r.Centers[b.Key].X + b.Width / 2.0;
        private static double Top(AlignDistributeResult r, LayoutBox b) => r.Centers[b.Key].Y + b.Height / 2.0;
        private static double Bottom(AlignDistributeResult r, LayoutBox b) => r.Centers[b.Key].Y - b.Height / 2.0;

        private static void AssertInsideAndApart(AlignDistributeResult r, List<LayoutBox> boxes, double width = W, double height = H)
        {
            foreach (var b in boxes)
            {
                Assert.True(Left(r, b) >= -1e-6 && Right(r, b) <= width + 1e-6, $"box {b.Key} leaves the width");
                Assert.True(Bottom(r, b) >= -1e-6 && Top(r, b) <= height + 1e-6, $"box {b.Key} leaves the height");
            }
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    bool apart = Right(r, boxes[i]) <= Left(r, boxes[j]) + 1e-6 || Right(r, boxes[j]) <= Left(r, boxes[i]) + 1e-6
                              || Top(r, boxes[i]) <= Bottom(r, boxes[j]) + 1e-6 || Top(r, boxes[j]) <= Bottom(r, boxes[i]) + 1e-6;
                    Assert.True(apart, $"boxes {boxes[i].Key} and {boxes[j].Key} overlap");
                }
        }

        [Fact]
        public void Off_DoesNothing()
        {
            var r = Run(TwoRows(), new AlignDistributeSettings { DistributeH = DistributeMode.EdgeToEdge });
            Assert.False(r.Applied);
            Assert.Empty(r.Centers);
            Assert.Equal(AlignFailReason.None, r.FailReason);
        }

        [Theory]
        [InlineData(GridRowAlign.Top)]
        [InlineData(GridRowAlign.Center)]
        [InlineData(GridRowAlign.Bottom)]
        public void RowsOnly_ViewsInARowShareTheLine(GridRowAlign align)
        {
            var boxes = TwoRows();
            var r = Run(boxes, new AlignDistributeSettings { AlignRows = align });

            Assert.True(r.Applied);
            Assert.Equal(2, r.Rows);
            Assert.Equal(3, r.Columns);
            foreach (var row in new[] { boxes.Take(3).ToList(), boxes.Skip(3).ToList() })
            {
                Func<LayoutBox, double> line = align == GridRowAlign.Top ? b => Top(r, b)
                    : align == GridRowAlign.Bottom ? b => Bottom(r, b)
                    : b => r.Centers[b.Key].Y;
                Assert.All(row, b => Assert.Equal(line(row[0]), line(b), 6));
            }
            // Not distributed: fixed gap, from the left, rows from the top.
            Assert.Equal(0, Left(r, boxes[0]), 6);
            Assert.Equal(Gap, Left(r, boxes[1]) - Right(r, boxes[0]), 6);
            Assert.Equal(H, boxes.Take(3).Max(b => Top(r, b)), 6);
            Assert.Equal(Gap, boxes.Take(3).Min(b => Bottom(r, b)) - boxes.Skip(3).Max(b => Top(r, b)), 6);
            AssertInsideAndApart(r, boxes);
        }

        [Fact]
        public void EdgeToEdge_FirstAndLastTouchTheAreaEdges_WithEqualGaps()
        {
            var boxes = TwoRows();
            var r = Run(boxes, new AlignDistributeSettings
            {
                AlignRows = GridRowAlign.Bottom, DistributeH = DistributeMode.EdgeToEdge, DistributeV = DistributeMode.EdgeToEdge
            });

            Assert.True(r.Applied);
            Assert.Equal(0, Left(r, boxes[0]), 6);
            Assert.Equal(W, Right(r, boxes[2]), 6);
            Assert.Equal(Left(r, boxes[1]) - Right(r, boxes[0]), Left(r, boxes[2]) - Right(r, boxes[1]), 6);
            Assert.Equal(H, Top(r, boxes[0]), 6);            // tallest of row 1 on the top edge
            Assert.Equal(0, Bottom(r, boxes[4]), 6);         // tallest of row 2 on the bottom edge
            AssertInsideAndApart(r, boxes);
        }

        [Fact]
        public void EqualSpacing_GapsToEdgesMatchGapsBetweenViews()
        {
            var boxes = TwoRows();
            var r = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Top, DistributeH = DistributeMode.EqualSpacing });

            double g = Left(r, boxes[0]);
            Assert.True(g > Gap);
            Assert.Equal(g, Left(r, boxes[1]) - Right(r, boxes[0]), 6);
            Assert.Equal(g, Left(r, boxes[2]) - Right(r, boxes[1]), 6);
            Assert.Equal(g, W - Right(r, boxes[2]), 6);
        }

        [Fact]
        public void EqualSpacing_TooTight_KeepsMinimumGapAndCentres()
        {
            // 3 x 180 + 2 x 20 = 580 of 600: equal spacing would be 15 < 20.
            var boxes = new List<LayoutBox> { Box(0, 0, 400, 180, 50), Box(1, 200, 400, 180, 50), Box(2, 400, 400, 180, 50) };
            var r = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Top, DistributeH = DistributeMode.EqualSpacing });

            Assert.True(r.Applied);
            Assert.Equal(Gap, Left(r, boxes[1]) - Right(r, boxes[0]), 6);
            Assert.Equal(10, Left(r, boxes[0]), 6);
            Assert.Equal(10, W - Right(r, boxes[2]), 6);
        }

        [Fact]
        public void Table_ColumnsAndRowsShareLines()
        {
            var boxes = TwoRows();
            var r = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Bottom, AlignColumns = GridColumnAlign.Left });

            Assert.True(r.Applied);
            Assert.Equal(2, r.Rows);
            Assert.Equal(3, r.Columns);
            for (int c = 0; c < 3; c++)
                Assert.Equal(Left(r, boxes[c]), Left(r, boxes[c + 3]), 6);
            Assert.All(boxes.Take(3), b => Assert.Equal(Bottom(r, boxes[0]), Bottom(r, b), 6));
            Assert.All(boxes.Skip(3), b => Assert.Equal(Bottom(r, boxes[3]), Bottom(r, b), 6));
            // Column 2 starts after column 1's widest view (100) plus the gap.
            Assert.Equal(100 + Gap, Left(r, boxes[1]), 6);
            AssertInsideAndApart(r, boxes);
        }

        [Fact]
        public void Table_CentreAndRight_AlignInsideTheColumn()
        {
            var boxes = TwoRows();
            var centre = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Center, AlignColumns = GridColumnAlign.Center });
            var right = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Top, AlignColumns = GridColumnAlign.Right });

            for (int c = 0; c < 3; c++)
            {
                Assert.Equal(centre.Centers[c].X, centre.Centers[c + 3].X, 6);
                Assert.Equal(Right(right, boxes[c]), Right(right, boxes[c + 3]), 6);
            }
        }

        [Fact]
        public void Table_TooWideForThePackedColumnCount_UsesFewerColumns()
        {
            // Packed as 3 + 3, but the table's columns (max widths 100 + 150 + 140 + gaps = 430) need more than 400.
            var boxes = TwoRows();
            var r = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Top, AlignColumns = GridColumnAlign.Left },
                width: 400, height: 400);

            Assert.True(r.Applied);
            Assert.Equal(2, r.Columns);
            Assert.Equal(3, r.Rows);
            AssertInsideAndApart(r, boxes, 400, 400);
        }

        [Fact]
        public void DoesNotFit_IsNotApplied_AndSaysWhichSide()
        {
            var boxes = TwoRows();
            var rows = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Top }, height: 200);
            Assert.False(rows.Applied);
            Assert.Empty(rows.Centers);
            Assert.Equal(AlignFailReason.HeightExceeded, rows.FailReason);
            Assert.Equal(240, rows.Needed, 6);      // 120 + 20 + 100
            Assert.Equal(200, rows.Available, 6);

            var table = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Top, AlignColumns = GridColumnAlign.Left },
                width: 400, height: 200);
            Assert.False(table.Applied);
            Assert.NotEqual(AlignFailReason.None, table.FailReason);
        }

        [Fact]
        public void RowsOnly_RowWiderThanTheArea_Wraps()
        {
            // A tall view with two small views stacked beside it: the packer's "row" holds views one above the other.
            var boxes = new List<LayoutBox>
            {
                Box(0, 0, 400, 250, 200), Box(1, 270, 400, 250, 90), Box(2, 270, 290, 250, 90),
            };
            var r = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Top });

            Assert.True(r.Applied);
            Assert.Equal(2, r.Rows);
            AssertInsideAndApart(r, boxes);
        }

        [Fact]
        public void ColumnsOnly_ViewsInAColumnShareTheLine_AndColumnsSpread()
        {
            // Two packed columns.
            var boxes = new List<LayoutBox>
            {
                Box(0, 0, 400, 120, 100), Box(1, 0, 280, 80, 60), Box(2, 0, 200, 100, 90),
                Box(3, 140, 400, 200, 150), Box(4, 140, 230, 160, 70),
            };
            var r = Run(boxes, new AlignDistributeSettings
            {
                AlignColumns = GridColumnAlign.Right, DistributeH = DistributeMode.EdgeToEdge, DistributeV = DistributeMode.EdgeToEdge
            });

            Assert.True(r.Applied);
            Assert.Equal(2, r.Columns);
            Assert.All(boxes.Take(3), b => Assert.Equal(120, Right(r, b), 6));
            Assert.All(boxes.Skip(3), b => Assert.Equal(W, Right(r, b), 6));
            Assert.Equal(H, Top(r, boxes[0]), 6);
            Assert.Equal(0, Bottom(r, boxes[2]), 6);
            Assert.Equal(0, Bottom(r, boxes[4]), 6);
            AssertInsideAndApart(r, boxes);
        }

        [Theory]
        [InlineData(BlockAlignmentH.Left, BlockAlignmentV.Top)]
        [InlineData(BlockAlignmentH.Center, BlockAlignmentV.Center)]
        [InlineData(BlockAlignmentH.Right, BlockAlignmentV.Bottom)]
        public void NotDistributed_BlockFollowsSheetPosition(BlockAlignmentH h, BlockAlignmentV v)
        {
            var boxes = TwoRows();
            var r = Run(boxes, new AlignDistributeSettings { AlignRows = GridRowAlign.Top, AlignColumns = GridColumnAlign.Left }, h, v);

            double left = boxes.Min(b => Left(r, b)), top = boxes.Max(b => Top(r, b));
            double tableW = 100 + 150 + 140 + 2 * Gap, tableH = 120 + 100 + Gap;
            Assert.Equal(h == BlockAlignmentH.Left ? 0 : h == BlockAlignmentH.Right ? W - tableW : (W - tableW) / 2.0, left, 6);
            Assert.Equal(v == BlockAlignmentV.Top ? H : v == BlockAlignmentV.Bottom ? tableH : H - (H - tableH) / 2.0, top, 6);
        }

        [Fact]
        public void SingleView_EdgeToEdge_StaysAtTheAnchor()
        {
            var boxes = new List<LayoutBox> { Box(0, 50, 300, 100, 80) };
            var r = Run(boxes, new AlignDistributeSettings
            {
                AlignRows = GridRowAlign.Top, DistributeH = DistributeMode.EdgeToEdge, DistributeV = DistributeMode.EdgeToEdge
            });

            Assert.True(r.Applied);
            Assert.Equal(0, Left(r, boxes[0]), 6);
            Assert.Equal(H, Top(r, boxes[0]), 6);
        }

        [Fact]
        public void RandomLayouts_NeverLeaveTheAreaOrOverlap()
        {
            var random = new Random(29);
            foreach (GridRowAlign rows in Enum.GetValues(typeof(GridRowAlign)))
            foreach (GridColumnAlign columns in Enum.GetValues(typeof(GridColumnAlign)))
            foreach (DistributeMode dh in Enum.GetValues(typeof(DistributeMode)))
            foreach (DistributeMode dv in Enum.GetValues(typeof(DistributeMode)))
            for (int run = 0; run < 20; run++)
            {
                var boxes = new List<LayoutBox>();
                int n = random.Next(1, 14);
                for (int i = 0; i < n; i++)
                {
                    double w = random.Next(30, 200), h = random.Next(30, 150);
                    boxes.Add(Box(i, random.Next(0, (int)(W - w)), random.Next((int)h, (int)H), w, h));
                }

                var r = Run(boxes, new AlignDistributeSettings { AlignRows = rows, AlignColumns = columns, DistributeH = dh, DistributeV = dv });
                if (!r.Applied)
                {
                    Assert.Empty(r.Centers);
                    continue;
                }
                Assert.Equal(n, r.Centers.Count);
                AssertInsideAndApart(r, boxes);
            }
        }
    }
}
