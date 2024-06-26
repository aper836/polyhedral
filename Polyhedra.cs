using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace polyhedral;


internal struct FaceVertex(MapPlane first, MapPlane second, MapPlane third) : IEquatable<FaceVertex>
{
    public ImmutableArray<MapPlane> Planes => [first, second, third];

    public override bool Equals(object? obj)
    {
        if(obj is not FaceVertex v)
        {
            return false;
        }
        return Equals(v);
    }

    public Vector3D<double> GetPoint()
    {
        if (!Geometry.GetIntersection(Planes[0].Plane, Planes[1].Plane, Planes[2].Plane, out Vector3D<double> p))
            throw new UnreachableException();

        return p;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Planes);
    }

    public bool Equals(FaceVertex other)
    {
        for (var i = 0; i < Planes.Length; i++)
        {
            if (other.Planes[i] != Planes[i])
            {
                return false;
            }
        }
        return true;
    }
    public static bool operator ==(FaceVertex a, FaceVertex b)
    {

        return a.Equals(b);
    }
    public static bool operator !=(FaceVertex a, FaceVertex b)
    {

        return !a.Equals(b);
    }
}
internal class FaceEdge
{
    public FaceVertex First { get; }

    public FaceVertex Second { get; }

    public ImmutableArray<MapPlane> Common { get; }

    internal FaceEdge(FaceVertex first, FaceVertex second, ImmutableArray<MapPlane> common)
    {
        First = first;
        Second = second;
        Common = common;
    }
    internal bool Same(FaceEdge edge)
    {
        return (edge.First == First 
            && edge.Second == Second) || (edge.First == Second && edge.Second == First);
    }
}

internal class Face
{
    internal Face(ImmutableArray<FaceVertex> vertices, ImmutableArray<FaceEdge> edges, MapPlane surface)
    {
        OrderedEdges = edges;
        OrderedPoints = vertices;
        Surface = surface;
    }
    public MapPlane Surface { get; }
    public ImmutableArray<FaceVertex> OrderedPoints { get; } = ImmutableArray<FaceVertex>.Empty;
    public ImmutableArray<FaceEdge> OrderedEdges { get; } = ImmutableArray<FaceEdge>.Empty;

    // split without raycasting edge per edge solves most issues with degeneracies...
    public (Face, Face) Split(MapPlane splitter)
    {
        var backFace = new List<FaceVertex>();
        var frontFace = new List<FaceVertex>();
        foreach (var edge in OrderedEdges) 
        {
            var sideFirst = Geometry.ComputeSide(edge.First.GetPoint(), splitter.Plane);
            var sideSecond = Geometry.ComputeSide(edge.Second.GetPoint(), splitter.Plane);

            if(sideFirst != sideSecond)
            {
                Console.WriteLine($"{sideFirst} == {sideSecond}");
                if (!Geometry.GetIntersection(edge.Common[0].Plane, edge.Common[1].Plane, splitter.Plane, out Vector3D<double> _))
                    Console.WriteLine("should not happen");

                backFace.Add(new FaceVertex(edge.Common[0], edge.Common[1], splitter));
                frontFace.Add(new FaceVertex(edge.Common[0], edge.Common[1], splitter));
            }
            if(sideFirst == PlaneSide.Back)
            {
                backFace.Add(edge.First);
            }
            if(sideFirst == PlaneSide.Front)
            {
                frontFace.Add(edge.First);
            }
            if(sideFirst == PlaneSide.Coplanar)
            {
                frontFace.Add(edge.First);
                backFace.Add(edge.First);
            }
        }
        return (Brush.BuildFace(Surface, backFace), Brush.BuildFace(Surface, frontFace));
    }
}

internal class Brush(ImmutableArray<Face> faces, ImmutableArray<FaceVertex> points, Box3D<double> bounds)
{
    public ImmutableArray<FaceVertex> Vertices => points;

    public ImmutableArray<Face> Faces => faces;

    public ImmutableArray<MapPlane> Volume => points.SelectMany(x => x.Planes).Distinct().ToImmutableArray();

    public Box3D<double> Bounds => bounds;
    
    private static List<FaceVertex> GetPointsOnPlane(IEnumerable<FaceVertex> vertices, MapPlane plane)
    {
        var points = new List<FaceVertex>();
        foreach(var p in vertices)
        {
            if (p.Planes.Contains(plane))
            {
                points.Add(p);
            }
        }
        return points;
    }
    
    
    public static List<Face> Clip(Face face, bool keepShared, ImmutableArray<MapPlane> volume, int idx)
    {
        if(idx >= volume.Length)
            return [];
        
        var plane = volume[idx];
        var points = face.OrderedPoints.Select(x => x.GetPoint()).Distinct().ToList();
        var classification = Geometry.Classify(points, plane.Plane);
        switch (classification)
        {
            case PlaneSide.CoplanarBack:
            case PlaneSide.Back:
                return Clip(face, keepShared, volume, idx + 1);

            case PlaneSide.Front:
            case PlaneSide.CoplanarFront:
                return [face];
            case PlaneSide.Coplanar:

                if (Vector3D.Dot(plane.Plane.Normal, face.Surface.Plane.Normal) > 0)
                {
                    if(!keepShared)
                        return [face];
                }

                return Clip(face, keepShared, volume, idx + 1);
            case PlaneSide.Spanning:
                var (back, front) = face.Split(plane);

                if (idx + 1 >= volume.Length)
                {
                    return [front];
                }

                var clipAgain = Clip(back, keepShared, volume, idx + 1);

                // totally inside brush
                if (clipAgain.Count == 0)
                    return [front];

                //ends up coplanar somewhere
                if (clipAgain[0] == back && clipAgain.Count == 1)
                    return [face];

                clipAgain.Insert(0, front);
                return clipAgain;
        }

        return [];
    }
    private static Brush CreateFromFaces(ImmutableArray<Face> faces, Box3D<double> bounds)
    {
        var points = faces.SelectMany(x => x.OrderedPoints).Distinct().ToImmutableArray();
        return new Brush(faces, points, bounds);
    }
    // construct all faces lying on a plane
    public static void UnionBrushes(List<Brush> polyhedrals)
    {
        var res = new List<Brush>();

        foreach(var polyhedral in polyhedrals)
        {
            var keepShared = false;
            var faces = polyhedral.Faces;
            var bounds = polyhedral.Bounds;
            foreach(var otherBrush in polyhedrals)
            {
                if (otherBrush == polyhedral)
                {
                    keepShared = true;
                    continue;
                }

                var pieces = new List<Face>();
                var shape = otherBrush.Volume;
                foreach (var f in faces)
                {
                    pieces.AddRange(Clip(f, keepShared, shape, 0));
                }
                faces = pieces.ToImmutableArray();
            }
            if(faces.Length > 0)
            {
                res.Add(CreateFromFaces(faces, bounds));
            }
        }

        polyhedrals.Clear();
        polyhedrals.AddRange(res);
    }
    private static void SetMinMax(ref Vector3D<double> min, ref Vector3D<double> max, Vector3D<double> pos)
    {
        double[] minValues = [min.X, min.Y, min.Z];
        double[] maxValues = [max.X, max.Y, max.Z];

        for (var i = 0; i < 3; i++)
        {
            if (pos[i] < min[i])
            {
                minValues[i] = pos[i];
            }
            if (pos[i] > max[i])
            {
                maxValues[i] = pos[i];
            }
        }

        min = new Vector3D<double>(minValues[0], minValues[1], minValues[2]);
        max = new Vector3D<double>(maxValues[0], maxValues[1], maxValues[2]);
    }

    public List<Polygon> GetPolygons()
    {
        var poly = new List<Polygon>();
        foreach(var face in Faces)
        {
            poly.Add(Polygon.CreatePolygonFromPoints(face.OrderedPoints.Select(p => p.GetPoint()).ToList(), face.Surface));
        }
        return poly;
    }

    internal static Face BuildFace(MapPlane plane, List<FaceVertex> vertices)
    {
        for (var i = 1; i < vertices.Count; i++)
        {
            var before = vertices[i - 1].GetPoint();
            var mid = vertices[i].GetPoint();
            var next = vertices[(i + 1) % vertices.Count].GetPoint();

            var beforeMid = before - mid;
            var nextMid = next - mid;

            var crossProd = Vector3D.Cross(beforeMid, nextMid);

            if(Vector3D.Dot(crossProd, plane.Plane.Normal) < 0)
            {
                (vertices[i], vertices[(i + 1) % vertices.Count]) = (vertices[(i + 1) % vertices.Count], vertices[i]);
            }
        }
        var orderedEdges = new List<FaceEdge>();
        for (var i = 0; i < vertices.Count; i++)
        {
            var current = vertices[i];
            var next = vertices[(i + 1) % vertices.Count];

            var currentWithoutCommon = current.Planes.ToList();
            var nextWithoutCommon = next.Planes.ToList();
            var arr = currentWithoutCommon.Intersect(nextWithoutCommon).ToImmutableArray();

            Debug.Assert(arr.Length == 2);

            orderedEdges.Add(new FaceEdge(current, next, arr));
        }

        return new Face(vertices.ToImmutableArray(), orderedEdges.ToImmutableArray(), plane);
    }

    internal static Brush CreateFrom(MapBrush brush)
    {
        var planes = brush.Planes;
        var poly = new List<FaceVertex>();

        var min = Vector3D<double>.One * double.MaxValue;
        var max = Vector3D<double>.One * double.MinValue;

        for (var i = 0; i < planes.Length - 2; i++)
        {
            for (var j = i + 1; j < planes.Length - 1; j++)
            {
                for(var k = j + 1; k < planes.Length; k++)
                {
                    var legal = true;
                    var intersection = Geometry.GetIntersection(planes[i].Plane, planes[j].Plane, planes[k].Plane, out Vector3D<double> p);

                    if (!intersection)
                        continue;

                    if (legal)
                    {
                        SetMinMax(ref min, ref max, p);

                        poly.Add(new FaceVertex(planes[i], planes[j], planes[k]));
                    }

                }
            }
        }
        var faces = new List<Face>();
        foreach(var pl in planes)
        {
            var points = GetPointsOnPlane(poly, pl);
            faces.Add(BuildFace(pl, points));
        }
        
        return new Brush(faces.ToImmutableArray(), poly.ToImmutableArray(), new Box3D<double>(min, max));
    }
}
