using System;
using System.Globalization;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Entities;
using Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Geometry;

namespace Revit26_Plugin_Revised.VoidOpenings.V001.Core.Domain.Services;

/// <summary>
/// The geometry of an opening in the structural model's own coordinates: where it is, which way it
/// points, its shape and its sizes. Two signatures are compared with tolerances (1 mm, half a
/// degree), so rounding never makes an unchanged opening look changed.
/// </summary>
public sealed class OpeningSignature
{
    private const double LengthToleranceMm = 1.0;
    private static readonly double MinAxisCos = Math.Cos(0.5 * Math.PI / 180);

    /// <summary>Centre of the opening.</summary>
    public Vec3 Center { get; }

    /// <summary>Cut direction.</summary>
    public Vec3 AxisZ { get; }

    /// <summary>Width direction.</summary>
    public Vec3 AxisX { get; }

    /// <summary>Shape.</summary>
    public OpeningShape Shape { get; }

    /// <summary>Width (rectangular) in millimetres.</summary>
    public double WidthMm { get; }

    /// <summary>Height (rectangular) in millimetres.</summary>
    public double HeightMm { get; }

    /// <summary>Diameter (circular) in millimetres.</summary>
    public double DiameterMm { get; }

    /// <summary>Depth in millimetres.</summary>
    public double DepthMm { get; }

    /// <summary>Creates a signature.</summary>
    public OpeningSignature(Vec3 center, Vec3 axisZ, Vec3 axisX, OpeningShape shape,
        double widthMm, double heightMm, double diameterMm, double depthMm)
    {
        Center = center;
        AxisZ = axisZ;
        AxisX = axisX;
        Shape = shape;
        WidthMm = widthMm;
        HeightMm = heightMm;
        DiameterMm = diameterMm;
        DepthMm = depthMm;
    }

    /// <summary>
    /// Signature of a calculated opening. The opening is in host coordinates;
    /// <paramref name="hostToStructural"/> brings it into the structural model's coordinates.
    /// </summary>
    public static OpeningSignature Of(OpeningData opening, Transform3 hostToStructural)
    {
        if (opening == null) throw new ArgumentNullException(nameof(opening));
        Frame3 frame = hostToStructural.Apply(opening.Orientation);
        return new OpeningSignature(frame.Origin, frame.AxisZ, frame.AxisX, opening.Shape,
            opening.WidthMm, opening.HeightMm, opening.DiameterMm, opening.DepthMm);
    }

    /// <summary>True when the two describe the same opening within tolerance. Opposite axis directions count as the same.</summary>
    public bool Matches(OpeningSignature other)
    {
        if (other == null || Shape != other.Shape)
            return false;
        if ((Center - other.Center).Length > LengthToleranceMm)
            return false;
        if (Math.Abs(AxisZ.Dot(other.AxisZ)) < MinAxisCos)
            return false;
        if (Shape == OpeningShape.Rectangular && Math.Abs(AxisX.Dot(other.AxisX)) < MinAxisCos)
            return false;
        return Near(WidthMm, other.WidthMm) && Near(HeightMm, other.HeightMm)
            && Near(DiameterMm, other.DiameterMm) && Near(DepthMm, other.DepthMm);
    }

    private static bool Near(double a, double b) => Math.Abs(a - b) <= LengthToleranceMm;

    /// <summary>The signature as one line of text, for storing in the model.</summary>
    public override string ToString() => string.Join(";", new[]
    {
        Shape == OpeningShape.Circular ? "C" : "R",
        F(Center.X), F(Center.Y), F(Center.Z),
        F(AxisZ.X, 6), F(AxisZ.Y, 6), F(AxisZ.Z, 6),
        F(AxisX.X, 6), F(AxisX.Y, 6), F(AxisX.Z, 6),
        F(WidthMm), F(HeightMm), F(DiameterMm), F(DepthMm),
    });

    /// <summary>Reads a signature written by <see cref="ToString"/>. Returns false for anything else.</summary>
    public static bool TryParse(string text, out OpeningSignature signature)
    {
        signature = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] parts = text.Split(';');
        if (parts.Length != 14 || (parts[0] != "C" && parts[0] != "R"))
            return false;

        var n = new double[13];
        for (int i = 0; i < 13; i++)
            if (!double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]))
                return false;

        signature = new OpeningSignature(new Vec3(n[0], n[1], n[2]), new Vec3(n[3], n[4], n[5]), new Vec3(n[6], n[7], n[8]),
            parts[0] == "C" ? OpeningShape.Circular : OpeningShape.Rectangular, n[9], n[10], n[11], n[12]);
        return true;
    }

    private static string F(double value, int decimals = 2)
        => Math.Round(value, decimals).ToString("0.######", CultureInfo.InvariantCulture);
}
