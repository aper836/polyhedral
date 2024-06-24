
using Silk.NET.Maths;
using System.Numerics;

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