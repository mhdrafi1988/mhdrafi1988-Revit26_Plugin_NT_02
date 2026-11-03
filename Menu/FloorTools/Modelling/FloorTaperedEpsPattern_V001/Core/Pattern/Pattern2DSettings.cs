namespace Revit26_Plugin_Revised.FloorTaperedEpsPattern.V001.Core.Pattern
{
    /// <summary>All inputs of the 2D pattern engine. Lengths in metres, thickness in mm.</summary>
    public sealed record Pattern2DSettings
    {
        public const double MinBoardSizeM = 0.25;
        public const double MaxBoardSizeM = 1.00;

        /// <summary>Board size along the grid direction (m).</summary>
        public double BoardLengthM { get; init; } = 1.0;

        /// <summary>Board size across the grid direction (m).</summary>
        public double BoardWidthM { get; init; } = 1.0;

        public double SlopePercent { get; init; } = 1.0;
        public double MinThicknessMm { get; init; } = 20.0;

        public GridDirectionMode GridDirection { get; init; } = GridDirectionMode.EachEdge;

        /// <summary>
        /// Index of the validated outline edge (CCW order, after collinear merge) the grid runs
        /// parallel to when <see cref="GridDirection"/> is <see cref="GridDirectionMode.SelectedEdge"/>.
        /// </summary>
        public int AlignEdgeIndex { get; init; }
        public GridOriginMode GridOrigin { get; init; } = GridOriginMode.BoundingBoxCorner;

        public bool DrawGrid { get; init; } = true;
        public bool DrawHipRidge { get; init; } = true;
        public bool DrawContours { get; init; } = true;
        public double ContourIntervalMm { get; init; } = 10.0;
        public LabelMode Labels { get; init; } = LabelMode.None;

        /// <summary>
        /// In "each edge" (contour-row) mode, draw a contour on every row line:
        /// interval = board width × slope (1.0 m × 1 % = 10 mm). <see cref="ContourIntervalMm"/> is then ignored.
        /// </summary>
        public bool ContoursFollowRows { get; init; } = true;

        /// <summary>Contour-rows mode: where rows start and where the leftover cut goes (A / B / C / D).</summary>
        public RowStartMode RowStart { get; init; } = RowStartMode.HipsWithMiddleFiller;

        /// <summary>Only for <see cref="RowStartMode.FromMiddle"/>: D1, D2 or Auto per face.</summary>
        public MiddleStartVariant MiddleVariant { get; init; } = MiddleStartVariant.Auto;

        /// <summary>Contour-rows mode: joints set out per row, aligned up the slope, or staggered.</summary>
        public JointPattern JointPattern { get; init; } = JointPattern.AsSetOut;

        /// <summary>How pieces are colour-coded with filled regions (None = no hatching).</summary>
        public HatchMode Hatch { get; init; } = HatchMode.None;

        /// <summary>Rectangular (length × width) or square (one size) boards.</summary>
        public BoardShape Shape { get; init; } = BoardShape.Rectangular;

        /// <summary>Square boards: laying preset (sets row start and joint pattern).</summary>
        public SquarePattern SquareLaying { get; init; } = SquarePattern.StackFromHips;

        /// <summary>Row start E: extra width of the hip piece along the row (m), for factory hip strips. 0 = triangle only.</summary>
        public double HipStripWidthM { get; init; }

        /// <summary>Per side, search the joint offset with the fewest slivers, then the most full boards (rectangular boards).</summary>
        public bool BestFitOffset { get; init; }

        /// <summary>Smallest acceptable cut piece along the row (m). Pieces narrower than this are "slivers".</summary>
        public double MinPieceM { get; init; } = MinBoardSizeM;

        public bool ContourIntervalLocked => ContoursFollowRows && GridDirection == GridDirectionMode.EachEdge;

        public double EffectiveContourIntervalMm =>
            ContourIntervalLocked ? BoardWidthM * SlopePercent * 10.0 : ContourIntervalMm;

        /// <summary>Segments shorter than this are dropped (m). The Revit layer raises it to ShortCurveTolerance.</summary>
        public double MinSegmentLengthM { get; init; } = GeometryTolerance.MinSegmentLength;

        /// <summary>
        /// The settings the engine actually uses: square boards get length = width and their laying
        /// preset's row start / joint pattern; best fit is not used with square presets.
        /// </summary>
        public Pattern2DSettings Normalized()
        {
            if (Shape != BoardShape.Square)
            {
                return this;
            }

            (RowStartMode start, JointPattern joints) = SquareLaying switch
            {
                SquarePattern.HalfBondFromHips => (RowStartMode.FromUpperHip, JointPattern.StaggerHalf),
                SquarePattern.CentreOutStack => (RowStartMode.FromMiddle, JointPattern.Aligned),
                SquarePattern.HipDiagonalPairs => (RowStartMode.FromUpperHip, JointPattern.AsSetOut),
                _ => (RowStartMode.FromUpperHip, JointPattern.Aligned)
            };

            return this with
            {
                BoardLengthM = BoardWidthM,
                GridDirection = GridDirectionMode.EachEdge,
                RowStart = start,
                MiddleVariant = MiddleStartVariant.Auto,
                JointPattern = joints,
                HipStripWidthM = 0,
                BestFitOffset = false
            };
        }

        public bool AnyLayerEnabled => DrawGrid || DrawHipRidge || DrawContours || Labels != LabelMode.None;
    }
}
