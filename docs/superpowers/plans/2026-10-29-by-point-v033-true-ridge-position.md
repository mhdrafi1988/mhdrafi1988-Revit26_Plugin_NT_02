# By Point V033 — Ridge Lines at the True Watershed — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** V033 draws ridge lines where the ridge really is: the line where two drain groups are equally far
away. On an open roof that is the Voronoi edge between drains, with ridges meeting at Voronoi vertices
(circumcentres of the drains' Delaunay triangles). Openings and roof edges bend it. Ridge points are no longer
snapped to existing shape points.

**Architecture:** Three Revit-free Core pieces, all unit-tested. `RoofOutline` holds the top-face loops as plan
polylines (inside test, line of sight, reflex corners). `GeodesicField` gives the distance from any roof point
to any drain group: a straight line, or round opening/edge corners when the line is blocked. `RidgeFinder` is
rewritten: it labels a fine grid with each node's nearest group, finds exact crossing points by bisection
where labels differ, solves 3-group junctions, chains and straightens the result, then extends open ends to
the roof edge. The Revit side only supplies the outline loops and draws the lines. Elevations are never changed.

**Tech Stack:** C# / .NET 8, Revit 2026 API (Infrastructure only), xUnit (`Tests/AutoSlopeByPoint.Tests`).

**Spec:** No spec file. The owner's request (2026-10-29): *"By Point V033 — update this: ridge point may be
approximately the Voronoi and Delaunay points (but can vary)"*. Owner's answers: **lines only, true position**
(draw lines, add no shape points); **update V033 in place → 33.1.0**.

## Global Constraints

- Folder: `Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033/` — change in place, no new tool folder.
- Namespace: `Revit26_Plugin_Revised.AutoSlopeByPoint.V033.Core.Services` for new Core files.
- `Core/` stays Revit-free: no `Autodesk.*` usings. Units are Revit internal feet; a mm constant is `mm / 304.8`.
- Elevations and shape points are never changed by ridge lines. Lines stay inside the runner's existing
  "Apply AutoSlope" transaction (one undo step).
- No change to settings files, the Excel column layout, the Run Summary "Ridge lines" row format, ribbon
  internal names, command class names or the add-in GUID.
- Tool version **33.1.0** (`ToolCatalog.AutoSlopeByPointV033` and `AutoSlopeConstants.FallbackToolVersion`);
  plugin `<Version>` **6.20.0**; `CHANGELOG.md` entry dated 2026-10-29 with a user note.
- `///` XML comments on every public class, method and property added or changed.
- Build: `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` → 0 errors.
  Tests: `dotnet test Tests/AutoSlopeByPoint.Tests` → all pass.
- The cloud sandbox has no `dotnet`. Install the .NET 8 SDK first:
  `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 && export PATH="$HOME/.dotnet:$PATH"`.
  If the download is blocked, stop and report it (network policy).

## Review Focus

1. **Drain on the roof edge or at a corner** (outlets at the parapet are common): it must still "see" the roof
   and produce ridges. → `RoofOutlineTests.IsVisible_FromABoundaryCorner`, `RidgeFinderTests.DrainsOnRoofEdge_BisectorEdgeToEdge`.
2. **Four drains on one circle** (a regular drain grid is the most common layout): one 4-way junction, not two
   close junctions joined by a stub. → `RidgeFinderTests.FourDrainsInASquare_OneFourWayJunction`.
3. **Ridge meets an opening**: it stops on the opening edge at the round-the-corner position (not the straight
   bisector) and never crosses the opening. → `RidgeFinderTests.Opening_RidgeBendsAndStopsAtTheOpening`.
4. **Drainage distance cut-off**: the ridge stops where sloping stops; it is not extended to the roof edge.
   → `RidgeFinderTests.MaxDistance_RidgeStopsWhereTheSlopeStops`.
5. **Big roof with a round opening** (tessellated arc → many corners): finishes in seconds and stays cancellable.
   → `RidgeFinderTests.LargeRoof_FinishesInSeconds`, `RidgeFinderTests.Cancelled_Throws`.

---

### Task 1: `RoofOutline` — roof loops, inside test, line of sight

**Files:**
- Create: `Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033/Core/Services/RoofOutline.cs`
- Modify: `Tests/AutoSlopeByPoint.Tests/AutoSlopeByPoint.Tests.csproj` (link the new file, same pattern as the existing links)
- Create: `Tests/AutoSlopeByPoint.Tests/TestRoofs.cs`
- Test: `Tests/AutoSlopeByPoint.Tests/RoofOutlineTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class RoofOutline`
    - `RoofOutline(IEnumerable<IReadOnlyList<Point3>> loops)` — closed plan polylines, any orientation. Drops a
      repeated closing point and consecutive duplicates (< 1e-9 ft); ignores loops with < 3 points; Z ignored.
    - `IReadOnlyList<IReadOnlyList<Point3>> Loops { get; }`
    - `double MinX, MinY, MaxX, MaxY { get; }` — plan bounding box of all loops.
    - `bool Contains(Point3 p)` — even-odd over all loops; within `OnEdgeTolerance` of an edge counts as inside.
    - `bool IsVisible(Point3 a, Point3 b)` — the segment stays inside the roof.
    - `bool IsClearRect(double x0, double y0, double x1, double y1)` — all 4 corners inside, no loop edge crosses
      or touches the rectangle's sides, no loop vertex inside it.
    - `Point3? RayHit(Point3 from, double dirX, double dirY, double maxLength)` — first point where the ray meets
      a loop edge at distance in (1e-9, maxLength]; direction need not be unit; null when none.
    - `IReadOnlyList<Point3> Pivots { get; }` — reflex corners (the roof's inside angle > 180°).
    - `public const double OnEdgeTolerance = 1e-6;`
- Test helper `internal static class TestRoofs` (in `TestRoofs.cs`):
  - `IReadOnlyList<Point3> Box(double x0, double y0, double x1, double y1)` — 4-point loop.
  - `IReadOnlyList<Point3> Circle(double cx, double cy, double r, int n)` — n-point loop.
  - `RoofOutline Rectangle(double w = 40, double h = 24, (double X0, double Y0, double X1, double Y1)? hole = null)`
  - `RoofOutline Polygon(params (double X, double Y)[] outer)`

- [ ] **Step 1: Install the .NET 8 SDK** (see Global Constraints) and run `dotnet test Tests/AutoSlopeByPoint.Tests`.
  Expected: the existing tests pass (baseline).

- [ ] **Step 2: Write the failing tests** in `RoofOutlineTests.cs` (hole = `(16, 8, 24, 14)` on the 40 × 24 rectangle):

```csharp
[Fact] public void Contains_InsideOutsideHoleAndEdge()
{
    var o = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
    Assert.True(o.Contains(new Point3(1, 1, 0)));
    Assert.False(o.Contains(new Point3(20, 11, 0)));   // in the opening
    Assert.False(o.Contains(new Point3(41, 1, 0)));    // outside
    Assert.True(o.Contains(new Point3(0, 5, 0)));      // on the outer edge
    Assert.True(o.Contains(new Point3(16, 11, 0)));    // on the opening edge
}

[Fact] public void IsVisible_OpeningBlocksOnlyLinesThroughIt()
{
    var o = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
    Assert.False(o.IsVisible(new Point3(10, 11, 0), new Point3(30, 11, 0)));
    Assert.True(o.IsVisible(new Point3(10, 2, 0), new Point3(30, 2, 0)));
    Assert.True(o.IsVisible(new Point3(10, 8, 0), new Point3(30, 8, 0)));   // runs along the opening edge
    Assert.False(o.IsVisible(new Point3(12, 4, 0), new Point3(20, 12, 0))); // through corner (16,8) into the opening
    Assert.True(o.IsVisible(new Point3(12, 4, 0), new Point3(16, 8, 0)));   // ends on that corner
}

[Fact] public void IsVisible_FromABoundaryCorner()
{
    var o = TestRoofs.Rectangle();
    Assert.True(o.IsVisible(new Point3(0, 0, 0), new Point3(20, 12, 0)));
    Assert.True(o.IsVisible(new Point3(0, 12, 0), new Point3(40, 12, 0)));  // edge to edge
}

[Fact] public void IsVisible_LShape_InnerCornerBlocks()
{
    var o = TestRoofs.Polygon((0, 0), (40, 0), (40, 10), (20, 10), (20, 24), (0, 24));
    Assert.False(o.IsVisible(new Point3(35, 5, 0), new Point3(5, 20, 0)));
}

[Fact] public void Pivots_AreTheReflexCornersOnly()
{
    var withHole = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
    Assert.Equal(4, withHole.Pivots.Count);
    Assert.All(withHole.Pivots, p => Assert.True((p.X is 16 or 24) && (p.Y is 8 or 14)));

    var l = TestRoofs.Polygon((0, 0), (40, 0), (40, 10), (20, 10), (20, 24), (0, 24));
    var pivot = Assert.Single(l.Pivots);
    Assert.Equal((20.0, 10.0), (pivot.X, pivot.Y));
}

[Fact] public void IsClearRect_RejectsCellsTouchingTheOutline()
{
    var o = TestRoofs.Rectangle(hole: (16, 8, 24, 14));
    Assert.True(o.IsClearRect(1, 1, 2, 2));
    Assert.False(o.IsClearRect(15, 9, 17, 10));         // crosses the opening edge
    Assert.False(o.IsClearRect(15.5, 7.5, 16.5, 8.5));  // opening corner inside
    Assert.False(o.IsClearRect(38, 1, 41, 2));          // corner outside the roof
}

[Fact] public void RayHit_FindsTheEdgeWithinReach()
{
    var o = TestRoofs.Rectangle();
    Point3? hit = o.RayHit(new Point3(39, 12, 0), 1, 0, 3);
    Assert.NotNull(hit);
    Assert.Equal(40, hit.Value.X, 9);
    Assert.Equal(12, hit.Value.Y, 9);
    Assert.Null(o.RayHit(new Point3(39, 12, 0), 1, 0, 0.5));
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test Tests/AutoSlopeByPoint.Tests --filter RoofOutlineTests`
Expected: build error, `RoofOutline` not found.

- [ ] **Step 4: Implement `RoofOutline`** in `Core/Services/RoofOutline.cs`. Rules the tests don't fully fix:
  - `IsVisible(a, b)`: false on any *proper* crossing (segment interiors intersect at one point). Collect every
    contact parameter `t` along a→b (touches at a vertex or an endpoint, both ends of a collinear overlap). Sort
    them with 0 and 1, and require `Contains` at the midpoint of every piece. Brute force over all edges with a
    bounding-box reject (no spatial index; YAGNI).
  - `Pivots`: the outer loop is the one with the largest |signed area|. Orient it CCW and every other loop CW, so
    the roof is always on the left. A vertex is a pivot when `cross(v − prev, next − v) < −1e-12` (right turn).
    Extra pivots never give wrong distances, only slower ones, so a second outer part is fine.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test Tests/AutoSlopeByPoint.Tests --filter RoofOutlineTests` → all PASS.

- [ ] **Step 6: Commit**

```bash
git add Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033/Core/Services/RoofOutline.cs Tests/AutoSlopeByPoint.Tests
git commit -m "By Point V033: RoofOutline (inside test, line of sight, reflex corners)"
```

---

### Task 2: `GeodesicField` — distance from any roof point to each drain group

**Files:**
- Create: `Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033/Core/Services/GeodesicField.cs`
- Modify: `Tests/AutoSlopeByPoint.Tests/AutoSlopeByPoint.Tests.csproj` (link it)
- Test: `Tests/AutoSlopeByPoint.Tests/GeodesicFieldTests.cs`

**Interfaces:**
- Consumes: `RoofOutline` (Task 1); `PathGraph`, `ShortestPathSolver.Solve` (existing, `Core/Services/ShortestPathSolver.cs`).
- Produces: `public sealed class GeodesicField`
  - `GeodesicField(RoofOutline outline, IReadOnlyList<Point3> drains, IReadOnlyList<int> drainGroup, CancellationToken cancel = default)`
  - `IReadOnlyList<int> Groups { get; }` — distinct group ids, ascending. `int GroupCount { get; }`
  - `double DistanceTo(Point3 p, int group)` — +∞ when `p` is outside the roof or sees no node of that group.
  - `int Nearest(Point3 p, out double distance)` — nearest group; a tie (≤ 1e-9) goes to the lower id; −1 and +∞ when none.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void OpenRoof_IsTheStraightLine()
{
    var f = new GeodesicField(TestRoofs.Rectangle(), new[] { new Point3(8, 12, 0) }, new[] { 0 });
    Assert.Equal(12, f.DistanceTo(new Point3(20, 12, 0), 0), 9);
}

[Fact] public void Opening_DistanceGoesRoundTheCorners()
{
    var f = new GeodesicField(TestRoofs.Rectangle(hole: (16, 8, 24, 14)), new[] { new Point3(20, 4, 0) }, new[] { 0 });
    Assert.Equal(6 + 8 * Math.Sqrt(2), f.DistanceTo(new Point3(20, 18, 0), 0), 6);
}

[Fact] public void Nearest_TieGoesToTheLowerGroup()
{
    var f = new GeodesicField(TestRoofs.Rectangle(),
        new[] { new Point3(8, 12, 0), new Point3(32, 12, 0) }, new[] { 1, 0 });
    Assert.Equal(0, f.Nearest(new Point3(20, 12, 0), out double d));
    Assert.Equal(12, d, 9);
    Assert.Equal(1, f.Nearest(new Point3(10, 12, 0), out _));
}

[Fact] public void OutsideTheRoof_IsUnreachable()
{
    var f = new GeodesicField(TestRoofs.Rectangle(hole: (16, 8, 24, 14)), new[] { new Point3(8, 12, 0) }, new[] { 0 });
    Assert.True(double.IsPositiveInfinity(f.DistanceTo(new Point3(20, 11, 0), 0)));
    Assert.Equal(-1, f.Nearest(new Point3(50, 11, 0), out double d));
    Assert.True(double.IsPositiveInfinity(d));
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test Tests/AutoSlopeByPoint.Tests --filter GeodesicFieldTests` → build error.

- [ ] **Step 3: Implement `GeodesicField`.** Nodes = the drains, then `outline.Pivots`. Build a `PathGraph` over the
  nodes with an edge for every pair where `outline.IsVisible`, weight = XY distance. Run `ShortestPathSolver.Solve`
  once per group, with that group's drain nodes as sources, giving `G[g][node]`. Then:
  - `DistanceTo(p, g)`: if `!Contains(p)` → +∞. Otherwise go through the nodes in ascending order of
    `|p − q| + G[g][q]`; the first one with `IsVisible(p, q)` gives the answer.
  - `Nearest(p)`: the same, ordered by `|p − q| + min_g G[g][q]`. The group is the argmin at that node. On a tie,
    keep going while values stay within 1e-9 and pick the lowest group id among visible nodes.
  This is exact when every shortest path bends only at drains or reflex corners, which holds in a polygon.
  It is the same distance the slope uses with the default max link length.

- [ ] **Step 4: Run tests** — `dotnet test Tests/AutoSlopeByPoint.Tests --filter GeodesicFieldTests` → all PASS.

- [ ] **Step 5: Commit**

```bash
git add Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033/Core/Services/GeodesicField.cs Tests/AutoSlopeByPoint.Tests
git commit -m "By Point V033: GeodesicField (distance round openings to each drain group)"
```

---

### Task 3: New `RidgeFinder` for two-group ridges, wired into the run

Replaces the shape-point method completely, so the main project builds green after this task.

**Files:**
- Modify (rewrite): `Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033/Core/Services/RidgeFinder.cs`
- Modify: `.../AutoSlopeByPoint_V033/Infrastructure/RoofOps/AutoSlopeGeometry.cs` (add `GetOutlineLoops`)
- Modify: `.../AutoSlopeByPoint_V033/Infrastructure/RoofOps/AutoSlopeRunner.cs` (step 3b, `FindRidges`, step 4 call)
- Modify: `.../AutoSlopeByPoint_V033/Infrastructure/RoofOps/RidgeLineService.cs` (`Place` signature, header)
- Modify: `.../AutoSlopeByPoint_V033/Core/Models/AutoSlopeConstants.cs` (delete `RidgeTieToleranceMm`, now unused)
- Delete: `Tests/AutoSlopeByPoint.Tests/SampleRoof.cs`
- Modify: `Tests/AutoSlopeByPoint.Tests/TestRoofs.cs` (add `Find`, `HitsHole`)
- Test (rewrite): `Tests/AutoSlopeByPoint.Tests/RidgeFinderTests.cs`

**Interfaces:**
- Consumes: `RoofOutline`, `GeodesicField` (Tasks 1–2).
- Produces (in `RidgeFinder.cs`, replacing the old types of the same names):
  - `public readonly record struct RidgeLine(Point3 A, Point3 B);` — plan points, Z = 0.
  - `public sealed class RidgeInput { RoofOutline Outline; IReadOnlyList<Point3> Drains; IReadOnlyList<int> DrainGroup; double MaxDistance = double.PositiveInfinity; }` (init-only properties)
  - `public sealed class RidgeResult { List<RidgeLine> Lines; List<Point3> Junctions; int GroupCount; int RidgeCount; double GridStep; }`
    (`RidgePoints` is removed: there are no vertex indices any more.)
  - `public static RidgeResult RidgeFinder.Find(RidgeInput input, CancellationToken cancel = default)`
  - Constants: `TargetGridNodes = 40_000`, `MinGridStepFt = 50 / 304.8`, `StraightenToleranceFt = 20 / 304.8`,
    `EndReachFactor = 3.0`, `JunctionMergeFactor = 0.5`. The old constants are deleted.
  - `AutoSlopeGeometry.GetOutlineLoops(Face topFace) → List<List<Point3>>`
  - `RidgeLineService.Place(Document doc, View activeView, RidgeResult ridges, AutoSlopeRequest request, Action<LogLevel, string> log)`
- Test helper additions: `TestRoofs.Find(RoofOutline o, (double X, double Y)[] drains, int[] groups = null, double maxDistance = double.PositiveInfinity, CancellationToken cancel = default)`
  (groups null = each drain its own group) and `TestRoofs.HitsHole(RidgeLine l, (double X0, double Y0, double X1, double Y1) hole)`
  (samples 50 points; true if any lies strictly inside, margin 1e-6).

- [ ] **Step 1: Write the failing tests** (two groups only; junctions come in Task 4). Local helper:
  `static bool Near(Point3 p, double x, double y, double tol) => Math.Abs(p.X - x) <= tol && Math.Abs(p.Y - y) <= tol;`

```csharp
[Fact] public void TwoDrains_OneLineOnTheBisector_EdgeToEdge()
{
    var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (32.0, 12.0) });
    var l = Assert.Single(r.Lines);
    Assert.Equal(20, l.A.X, 3); Assert.Equal(20, l.B.X, 3);
    Assert.Equal(new[] { 0.0, 24.0 }, new[] { l.A.Y, l.B.Y }.OrderBy(y => y).Select(y => Math.Round(y, 3)));
    Assert.Equal(1, r.RidgeCount); Assert.Equal(2, r.GroupCount); Assert.Empty(r.Junctions);
}

[Fact] public void TwoDrains_OffGrid_IsNotSnapped()          // midline x = 19.5, between any shape points
{
    var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (31.0, 12.0) }).Lines);
    Assert.Equal(19.5, l.A.X, 3); Assert.Equal(19.5, l.B.X, 3);
}

[Fact] public void TwoDrains_Diagonal_OneStraightLine()      // bisector y = 48 − 2x
{
    var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (6.0, 6.0), (30.0, 18.0) }).Lines);
    var ends = new[] { l.A, l.B }.OrderBy(p => p.Y).ToArray();
    Assert.True(Near(ends[0], 24, 0, 1e-3) && Near(ends[1], 12, 24, 1e-3));
}

[Fact] public void DrainsOnRoofEdge_BisectorEdgeToEdge()
{
    var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (0.0, 12.0), (40.0, 12.0) }).Lines);
    Assert.Equal(20, l.A.X, 3); Assert.Equal(20, l.B.X, 3);
}

[Fact] public void SameGroup_NoRidgeBetweenThem()
{
    var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (32.0, 12.0) }, new[] { 0, 0 });
    Assert.Empty(r.Lines); Assert.Equal(1, r.GroupCount);
}

[Fact] public void GroupOfTwo_RidgeKinksBetweenItsDrains()   // kink near (19.25, 12), ends (22.25, 0) and (22.25, 24)
{
    var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 6.0), (8.0, 18.0), (32.0, 12.0) }, new[] { 0, 0, 1 });
    Assert.Equal(2, r.Lines.Count); Assert.Equal(1, r.RidgeCount); Assert.Empty(r.Junctions);
    var pts = r.Lines.SelectMany(x => new[] { x.A, x.B }).ToList();
    Assert.Contains(pts, p => Near(p, 22.25, 0, 0.05));    // kept kink point sits within h of the true kink,
    Assert.Contains(pts, p => Near(p, 22.25, 24, 0.05));   // so the extended ends can drift slightly
    Assert.Contains(pts, p => Near(p, 19.25, 12, 0.25));
}

[Fact] public void Opening_RidgeBendsAndStopsAtTheOpening()
{
    var hole = (16.0, 8.0, 24.0, 14.0);
    var r = TestRoofs.Find(TestRoofs.Rectangle(hole: hole), new[] { (20.0, 4.0), (20.0, 20.0) });
    Assert.NotEmpty(r.Lines);
    Assert.DoesNotContain(r.Lines, l => TestRoofs.HitsHole(l, hole));
    var pts = r.Lines.SelectMany(x => new[] { x.A, x.B }).ToList();
    Assert.Contains(pts, p => Near(p, 0, 12, 1e-2));
    Assert.Contains(pts, p => Near(p, 40, 12, 1e-2));
    Assert.Contains(pts, p => Near(p, 16, 11, 0.05));   // round-the-corner tie, not y = 12
    Assert.Contains(pts, p => Near(p, 24, 11, 0.05));
}

[Fact] public void MaxDistance_RidgeStopsWhereTheSlopeStops()  // |y − 12| ≤ 5 when 13 from both drains
{
    var l = Assert.Single(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0), (32.0, 12.0) }, maxDistance: 13).Lines);
    var ys = new[] { l.A.Y, l.B.Y }.OrderBy(y => y).ToArray();
    Assert.InRange(ys[0], 6.5, 7.5); Assert.InRange(ys[1], 16.5, 17.5);
}

[Fact] public void OneDrain_NoLines() =>
    Assert.Empty(TestRoofs.Find(TestRoofs.Rectangle(), new[] { (8.0, 12.0) }).Lines);

[Fact] public void BadInput_Throws()
{
    Assert.Throws<ArgumentException>(() => RidgeFinder.Find(new RidgeInput { Drains = new Point3[0], DrainGroup = new int[0] }));
    Assert.Throws<ArgumentException>(() => RidgeFinder.Find(new RidgeInput
        { Outline = TestRoofs.Rectangle(), Drains = new[] { new Point3(1, 1, 0) }, DrainGroup = new int[0] }));
}

[Fact] public void Cancelled_Throws() =>
    Assert.Throws<OperationCanceledException>(() => TestRoofs.Find(TestRoofs.Rectangle(),
        new[] { (8.0, 12.0), (32.0, 12.0) }, cancel: new CancellationToken(true)));
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test Tests/AutoSlopeByPoint.Tests --filter RidgeFinderTests` → build errors (new API).

- [ ] **Step 3: Rewrite `RidgeFinder.Find`.** Update the file header to describe these steps:
  1. **Validate:** null `Outline`, `Drains` or `DrainGroup`, different lengths, or a negative group → `ArgumentException`.
     `cancel.ThrowIfCancellationRequested()` first, then once per grid row and per 256 cells.
     Build the `GeodesicField`. `GroupCount = field.GroupCount`. Fewer than 2 groups → return the empty result.
  2. **Grid:** step `h = max(sqrt(bboxArea / TargetGridNodes), MinGridStepFt)` over the outline's bounding box,
     stored in `GridStep`. Node label = `Nearest(p)` when `Contains(p)` and distance ≤ `MaxDistance`, else −1.
  3. **Crossings:** only cells where `IsClearRect` holds and at least 2 different labels ≥ 0 appear. On each cell
     side whose ends have labels `a ≠ b` (both ≥ 0), bisect `f = DistanceTo(p, a) − DistanceTo(p, b)` until the
     interval is < 1e-5 ft (at most 40 steps; a NaN stops it at the midpoint). Key the crossing by its side, so
     neighbouring cells share it. Tag it with the pair (min, max).
  4. **Segments (2 labels):** 2 crossings → one segment. 4 crossings (saddle) → label the cell centre. Join the
     two crossings beside each corner whose label differs from the centre's. An odd count (a −1 corner) → none.
     Cells with 3+ labels are skipped here; Task 4 handles them.
  5. **Chain and straighten:** drop zero-length segments. Walk runs between nodes of degree ≠ 2 (closed loops
     start anywhere), then apply Douglas–Peucker with `StraightenToleranceFt`. The current `Straighten` /
     `Simplify` / `DistanceToSegment` can stay, but they work on points, not indices.
  6. **Reach the roof edge:** a line end used by exactly one line and not a junction is extended. Use `RayHit`
     from that end, in the direction away from the line's other end, within `EndReachFactor × h`. On a hit,
     move the end there.
  7. `RidgeCount` = distinct pairs among the non-zero segments. Output order: in the order the runs were walked.
  Delete the old shape-point code: `StraightLinks`, `IsShortLink`, `Nearest`, `TypicalSpacing`, `JoinRidgePoints`, `Higher`.

- [ ] **Step 4: Run tests** — `dotnet test Tests/AutoSlopeByPoint.Tests` → all PASS (SettingsTests included).

- [ ] **Step 5: Wire the Revit side.**
  - `AutoSlopeGeometry.GetOutlineLoops(Face topFace)`: for each `CurveLoop` from `topFace.GetEdgesAsCurveLoops()`,
    add each curve's `Tessellate()` points except its last (the next curve's first), with Z = 0. Any exception, or
    a null face → empty list.
  - `AutoSlopeRunner`: step 3b becomes `ridges = FindRidges(prep, drains, request, log, cancel)`, and `ridgePositions`
    goes away. `FindRidges` keeps the "drain groups" Info line and the `< 2 groups` Warning. It then builds the
    drains from `drains.GroupOfDrain` (ordered by key: position `prep.Positions[key].ToPoint3()`, group = value),
    the outline from `GetOutlineLoops(prep.TopFace)`, and `MaxDistance = RevitConvert.MToFt(request.MaxDrainDistanceM)`.
    An empty outline gives Warning `"Ridge lines skipped: the roof outline could not be read. The slope is applied as usual."` and null.
    The success log becomes
    `$"Ridge lines: {r.Lines.Count} line(s), {r.Junctions.Count} junction(s), {r.RidgeCount} ridge(s), grid {RevitConvert.FtToMm(r.GridStep):0} mm, found in {clock.Elapsed.TotalSeconds:0.0} s."`.
    Keep the existing catch → Warning → null.
  - `RidgeLineService.Place`: drop the `positions` parameter; use `line.A` / `line.B`. In the header, say lines
    run between watershed points, not shape points.
  - Delete `AutoSlopeConstants.RidgeTieToleranceMm`.

- [ ] **Step 6: Build** — `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` → 0 errors.

- [ ] **Step 7: Commit**

```bash
git add -A Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033 Tests/AutoSlopeByPoint.Tests
git commit -m "By Point V033: ridge lines at the true watershed, not snapped to shape points"
```

---

### Task 4: Junctions where three or more groups meet

**Files:**
- Modify: `.../AutoSlopeByPoint_V033/Core/Services/RidgeFinder.cs`
- Test: `Tests/AutoSlopeByPoint.Tests/RidgeFinderTests.cs`

**Interfaces:**
- Consumes: Task 3's `Find` pipeline and `GeodesicField.DistanceTo`.
- Produces: `RidgeResult.Junctions` filled; junctions are line ends shared by 3+ lines.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void ThreeDrains_JunctionIsTheCircumcentre()   // Voronoi vertex = Delaunay circumcentre
{
    var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (6.0, 6.0), (34.0, 8.0), (18.0, 20.0) });
    var j = Assert.Single(r.Junctions);
    Assert.True(Near(j, 14768.0 / 736, 4480.0 / 736, 1e-3), $"junction at {j}");
    Assert.Equal(3, r.Lines.Count); Assert.Equal(3, r.RidgeCount);
    Assert.All(r.Lines, l => Assert.True(Near(l.A, j.X, j.Y, 1e-6) || Near(l.B, j.X, j.Y, 1e-6)));
}

[Fact] public void FourDrainsInASquare_OneFourWayJunction()
{
    var r = TestRoofs.Find(TestRoofs.Rectangle(), new[] { (10.0, 6.0), (30.0, 6.0), (10.0, 18.0), (30.0, 18.0) });
    var j = Assert.Single(r.Junctions);
    Assert.True(Near(j, 20, 12, 1e-3));
    Assert.Equal(4, r.Lines.Count); Assert.Equal(4, r.RidgeCount);
}

[Fact] public void LargeRoof_FinishesInSeconds()
{
    var holes = new[] { TestRoofs.Box(30, 20, 40, 30), TestRoofs.Box(80, 60, 95, 70),
                        TestRoofs.Box(110, 15, 120, 40), TestRoofs.Circle(150, 50, 6, 72) };
    var outline = new RoofOutline(new[] { TestRoofs.Box(0, 0, 200, 100) }.Concat(holes));
    var rnd = new Random(33);
    var drains = new List<(double X, double Y)>();
    while (drains.Count < 30)                       // each its own group, ≥ 1 away from every opening's box
    {
        double x = 2 + rnd.NextDouble() * 196, y = 2 + rnd.NextDouble() * 96;
        if (holes.All(h => x < h.Min(q => q.X) - 1 || x > h.Max(q => q.X) + 1
                        || y < h.Min(q => q.Y) - 1 || y > h.Max(q => q.Y) + 1)) drains.Add((x, y));
    }
    var watch = Stopwatch.StartNew();
    RidgeResult r = TestRoofs.Find(outline, drains.ToArray());
    Assert.NotEmpty(r.Lines); Assert.NotEmpty(r.Junctions);
    Assert.True(watch.Elapsed.TotalSeconds < 10, $"took {watch.Elapsed.TotalSeconds:0.0} s");
}
```

- [ ] **Step 2: Run to verify they fail** — the junction tests FAIL (no junctions yet; ends meet at a gap).

- [ ] **Step 3: Implement junctions in `Find`** (between steps 4 and 5 of Task 3):
  - A clear cell with 3+ labels ≥ 0 gets a junction J. Take the three lowest group ids `a < b < c` among the
    labels. Solve `DistanceTo(J, a) = DistanceTo(J, b) = DistanceTo(J, c)` with Newton:
    `F = (Da − Db, Da − Dc)`, central-difference Jacobian with step `h × 1e-3`, start at the mean of the cell's
    crossings, at most 20 iterations, stop when the step is < 1e-7 ft. Keep J only if it converged and lies
    inside the cell grown by `h` on each side and inside the roof. Otherwise use the mean of the crossings.
  - Add a segment from J to every crossing of the cell, tagged with that crossing's pair.
  - Junctions closer than `JunctionMergeFactor × h` merge into one node at their mean; segments between merged
    nodes become zero-length and are dropped. `Junctions` = the merged nodes that end 3+ segments, ascending by (X, Y).
  - Junctions are never extended (Task 3, step 6 already skips them).

- [ ] **Step 4: Run tests** — `dotnet test Tests/AutoSlopeByPoint.Tests` → all PASS.

- [ ] **Step 5: Build** — main project build → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033/Core/Services/RidgeFinder.cs Tests/AutoSlopeByPoint.Tests
git commit -m "By Point V033: ridges meet at the drains' Voronoi vertex"
```

---

### Task 5: Version 33.1.0 and release notes

**Files:**
- Modify: `Shared/Services/ToolCatalog.cs` (`AutoSlopeByPointV033` → `"33.1.0"`)
- Modify: `.../AutoSlopeByPoint_V033/Core/Models/AutoSlopeConstants.cs` (`FallbackToolVersion = "33.1.0"`)
- Modify: `Revit26_Plugin_Revised.csproj` (`<Version>6.20.0</Version>`)
- Modify: `Menu/00_Push_Button_Menu_items/Ribbon/RoofToolsRibbon.cs` (V033 tooltip text only)
- Modify: `.../AutoSlopeByPoint_V033/README_V033.md` ("Current version", "Ridge lines" section)
- Modify: `CHANGELOG.md` (new `## [6.20.0] — 2026-10-29` above 6.19.0)

- [ ] **Step 1: Bump versions** in the three files above.

- [ ] **Step 2: Ribbon tooltip** for `Btn_AutoSlopeByPoint_033`: `"V032 plus optional ridge lines: detail lines along the watershed between drain groups."`
  The button name, label and command are unchanged.

- [ ] **Step 3: README_V033.md** — set "Current version: **33.1.0**" and replace the "How the ridges are found"
  list with Tasks 1–4's method in plain words: distance round openings, nearest-group grid, exact crossings,
  junction at the Voronoi vertex, straightened, ends reach the roof edge or opening, stops at the drainage
  distance. Keep the "How they are placed" list, and remove "Every line starts and ends on a ridge point".

- [ ] **Step 4: CHANGELOG.md**

```markdown
## [6.20.0] — 2026-10-29

### Changed
- **Auto Slope By Point v33.1.0 (V033)** — ridge lines are drawn where the ridge really is: the line where two
  drain groups are equally far away. On an open roof this is the line halfway between neighbouring drains, and
  ridges meet at the point equally far from three drains. Near openings the ridge bends, because water goes
  round the opening. Ridge lines now run from roof edge to roof edge (or to an opening), stop where the
  drainage distance ends, and no longer jump between shape points.
- **User note:** elevations are unchanged; the slope is exactly what 33.0.0 calculates. Ridge lines can now
  sit between shape points. Revit builds the roof surface from the shape points, so on a roof with few points
  the surface may not show a sharp ridge exactly under the line.
```

- [ ] **Step 5: Verify** — `dotnet build Revit26_Plugin_Revised.csproj -p:EnableWindowsTargeting=true` → 0 errors;
  `dotnet test Tests/AutoSlopeByPoint.Tests` → all PASS.

- [ ] **Step 6: Commit**

```bash
git add Shared/Services/ToolCatalog.cs Revit26_Plugin_Revised.csproj CHANGELOG.md Menu/00_Push_Button_Menu_items/Ribbon/RoofToolsRibbon.cs Menu/RoofTools/RoofSlope/ByPoints/AutoSlopeByPoint_V033
git commit -m "Auto Slope By Point 33.1.0: ridge lines at the true watershed (plugin 6.20.0)"
```

**Check in Revit after merge** (no unit test covers it): run V033 with ridge lines on a roof processed by 33.0.0,
with 2, 3 and 4 drains and one opening. Lines should land on the halfway lines, meet at one point, reach the
roof edge, and Ctrl+Z should remove slope and lines together.
