
using Silk.NET.Maths;
using System.Collections.Immutable;
using System.Diagnostics;

namespace polyhedral;

public static class Geometry
{
    public static bool CastRay(Ray3D<double> ray, Plane<double> plane, out Vector3D<double> point)
    {
        point = default;
        var denom = Vector3D.Dot(plane.Normal, ray.Direction);
        if (Math.Abs(denom) < 1e-6)
        {
            return false; // No intersection, ray is parallel to the plane
        }

        double t = Vector3D.Dot(plane.Normal, -(plane.Distance * plane.Normal) - ray.Origin) / denom;

        // Calculate intersection point
        point = ray.Origin + t * ray.Direction;

        return true;
    }

    public static ImmutableArray<int> ComputeSides(IList<Vector3D<double>> points, Plane<double> plane)
    {
        int[] sides = { 0, 0, 0 };
        foreach (var point in points)
        {
            sides[(int)ComputeSide(point, plane)]++;
        }
        return sides.ToImmutableArray();
    }

    public static PlaneSide Classify(IList<Vector3D<double>> points, Plane<double> plane)
    {
        var sides = ComputeSides(points, plane);
        if (sides[(int)PlaneSide.Back] == points.Count)
        {
            return PlaneSide.Back;
        }
        if (sides[(int)PlaneSide.Front] == points.Count)
        {
            return PlaneSide.Front;
        }
        if (sides[(int)PlaneSide.Coplanar] == points.Count)
        {
            return PlaneSide.Coplanar;
        }
        if (sides[(int)PlaneSide.Back] > 0 && sides[(int)PlaneSide.Front] > 0)
        {
            return PlaneSide.Spanning;
        }
        if (sides[(int)PlaneSide.Coplanar] > 0 && sides[(int)PlaneSide.Front] > 0)
        {
            return PlaneSide.CoplanarFront;
        }
        if (sides[(int)PlaneSide.Coplanar] > 0 && sides[(int)PlaneSide.Back] > 0)
        {
            return PlaneSide.CoplanarBack;
        }
        
        throw new NotImplementedException();
    }
    
    
    public static PlaneSide Classify(Plane<double> plane, IList<Vector3D<double>> points)
    {
        var sides = ComputeSides(plane, points);
        if (sides[(int)PlaneSide.Back] == points.Count)
        {
            return PlaneSide.Back;
        }
        if (sides[(int)PlaneSide.Front] == points.Count)
        {
            return PlaneSide.Front;
        }
        if (sides[(int)PlaneSide.Coplanar] == points.Count)
        {
            return PlaneSide.Coplanar;
        }
        if (sides[(int)PlaneSide.Coplanar] > 0 && sides[(int)PlaneSide.Front] > 0)
        {
            return PlaneSide.CoplanarFront;
        }
        if (sides[(int)PlaneSide.Coplanar] > 0 && sides[(int)PlaneSide.Back] > 0)
        {
            return PlaneSide.CoplanarBack;
        }
        if (sides[(int)PlaneSide.Back] > 0 && sides[(int)PlaneSide.Front] > 0)
        {
            return PlaneSide.Spanning;
        }
        throw new NotImplementedException();
    }
    public static PlaneSide ComputeSide(Vector3D<double> point, Plane<double> plane)
    {
        var d = Plane.Dot(plane, new Vector4D<double>(point, 1)) * -1;

        const double SIDEEPSILON = 1e-3;
        if (d < -SIDEEPSILON)
        {
            return PlaneSide.Back;
        }
        if (d > SIDEEPSILON)
        {
            return PlaneSide.Front;
        }
        return PlaneSide.Coplanar;
    }

    public static ImmutableArray<int> ComputeSides(Plane<double> plane, IEnumerable<Vector3D<double>> points)
    {
        int[] sides = { 0, 0, 0 };
        foreach (var point in points)
        {
            sides[(int)ComputeSide(point, plane)]++;
        }
        return sides.ToImmutableArray();
    }
    public static Vector2D<double> TransformPoint(Vector3D<double> vec, MapPlane plane)
    {
        var x = Vector3D.Dot(plane.Tangent, vec);
        var y = Vector3D.Dot(plane.Bitangent, vec);

        return new Vector2D<double>(x, y);
    }
    public static Vector2D<double> FindCentroid(IEnumerable<Vector2D<double>> points)
    {
        var centroid = Vector2D<double>.Zero;
        var i = 0;
        foreach (var p in points)
        {
            centroid += p;
            i++;
        }
        return centroid / i;
    }


    public static bool GetIntersection(Plane<double> p0, Plane<double> p1, Plane<double> p2, out Vector3D<double> point)
    {
        const double EPSILON = 1e-7;

        var det = Vector3D.Dot(Vector3D.Cross(p0.Normal, p1.Normal), p2.Normal);
        if (Math.Abs(det) < EPSILON)
        {
            point = Vector3D<double>.Zero;
            return false;
        }

        point =
            (-(p0.Distance * Vector3D.Cross(p1.Normal, p2.Normal)) -
            (p1.Distance * Vector3D.Cross(p2.Normal, p0.Normal)) -
            (p2.Distance * Vector3D.Cross(p0.Normal, p1.Normal))) / det;

        return true;
    }
}