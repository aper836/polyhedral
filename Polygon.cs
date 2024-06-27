using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Formats.Tar;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace polyhedral;


public enum PlaneSide
{
    Coplanar,
    Front,
    Back,
    Spanning,
    CoplanarFront,
    CoplanarBack
}

internal class Polygon(ImmutableArray<Vector3D<double>> points, MapPlane plane)
{
    public MapPlane Surface { get; set; } = plane;
    public ImmutableArray<Vector3D<double>> Points { get; set; } = points;
    private static Vector2D<double> TransformPoint(Vector3D<double> vec, MapPlane plane)
    {
        var x = Vector3D.Dot(plane.Tangent, vec);
        var y = Vector3D.Dot(plane.Bitangent, vec);

        return new Vector2D<double>(x, y);
    }
    private static Vector2D<double> FindCentroid(IEnumerable<Vector2D<double>> points)
    {
        var centroid = Vector2D<double>.Zero;
        var i = 0;
        foreach(var p in points)
        {
            centroid += p;
            i++;
        }
        return (centroid / i) + Vector2D<double>.One * 1e-4;
    }

    public static Polygon CreatePolygonFromPoints(List<Vector3D<double>> points, MapPlane plane)
    {
        var centroid = FindCentroid(points.Select(x => TransformPoint(x, plane)));
        
        points.Sort((a, b) =>
        {
            var aplane = TransformPoint(a, plane);
            var bplane = TransformPoint(b, plane);

            var angle1 = Math.Atan2(aplane.Y - centroid.Y, aplane.X - centroid.X);
            var angle2 = Math.Atan2(bplane.Y - centroid.Y, bplane.X - centroid.X);

            return angle1.CompareTo(angle2);
        });

        return new Polygon(points.ToImmutableArray(), plane);
    }
    public List<(Vector3D<float>, Vector3D<float>, Vector3D<float>)> Triangulate()
    {
        var end = new List<(Vector3D<float>, Vector3D<float>, Vector3D<float>)>();
        Vector3D<double> firstVertex = Points[0];
        for (int i = 1; i < Points.Length - 1; i++)
        {
            Vector3D<double> vertex1 = Points[i];
            Vector3D<double> vertex2 = Points[i + 1];
            end.Add((firstVertex.As<float>(), vertex1.As<float>(), vertex2.As<float>()));
        }
        Debug.Assert(end.Count != 0);
        return end;
    }
    public static Polygon Create(Plane<double> plane, double size)
    {
        var right = Vector3D.Cross(plane.Normal, Vector3D<double>.UnitY);
        if(right == Vector3D<double>.Zero)
        {
            right = Vector3D.Cross(plane.Normal, Vector3D<double>.UnitX);
        }
        if(right == Vector3D<double>.Zero)
        {
            right = Vector3D.Cross(plane.Normal, Vector3D<double>.UnitZ);
        }
        var point = plane.Normal * -plane.Distance;
        var left = Vector3D.Cross(right, plane.Normal);
        
        var mapPlane = new MapPlane(plane, left, right);
        var p0 = right * size + left * size + point;
        var p1 = - right * size - left * size + point;
        var p2 = - right * size + left * size + point;
        var p3 =  right * size - left * size + point;

        return CreatePolygonFromPoints([p0, p1, p2, p3], mapPlane);
    }
    public PlaneSide Classify(Plane<double> plane)
    {
        var sides = ComputeSides(plane);
        if (sides[(int)PlaneSide.Back] == Points.Length)
        {
            return PlaneSide.Back;
        }
        if (sides[(int)PlaneSide.Front] == Points.Length)
        {
            return PlaneSide.Front;
        }
        if (sides[(int)PlaneSide.Coplanar] == Points.Length)
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
    private static PlaneSide ComputeSide(Vector3D<double> point, Plane<double> plane)
    {
        var d = Plane.Dot(plane, new Vector4D<double>(point, 1)) * -1;

        const double SIDEEPSILON = 1e-6;
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

    public ImmutableArray<int> ComputeSides(Plane<double> plane)
    {
        int[] sides = { 0, 0, 0 };
        foreach (var point in Points)
        {
            sides[(int)ComputeSide(point, plane)]++;
        }
        return sides.ToImmutableArray();
    }

    public (Polygon, Polygon) Split(Plane<double> plane)
    {
        var back = new List<Vector3D<double>>();
        var front = new List<Vector3D<double>>();

        for (var i = 0; i < Points.Length; i++)
        {
            var next = (i + 1) % Points.Length;
            var point = Points[i];
            var nextPoint = Points[next];
            var dir = Vector3D.Normalize(nextPoint - point);

            switch(ComputeSide(point, plane))
            {
                case PlaneSide.Back:
                    back.Add(point); 
                    break;
                case PlaneSide.Front:
                    front.Add(point);
                    break;
                case PlaneSide.Coplanar:
                    back.Add(point);
                    front.Add(point);
                    break;
            }
            var ray = new Ray3D<double>(point, dir);
            if(Geometry.CastRay(ray, plane, out Vector3D<double> p))
            {
                back.Add(p);
                front.Add(p);
            }
        }

        Polygon backPoly;
        Polygon frontPoly;

        Debug.Assert(back.Count >= 3);
        Debug.Assert(front.Count >= 3);

        backPoly = new Polygon(back.ToImmutableArray(), Surface);
        frontPoly = new Polygon(front.ToImmutableArray(), Surface);
        
        return (backPoly, frontPoly);
    }
}
