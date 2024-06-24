using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace polyhedral;

internal class Polyhedral(ImmutableArray<Polygon> polygons, Box3D<double> bounds)
{
    public ImmutableArray<Polygon> Polygons => polygons;

    public Box3D<double> Bounds => bounds;
    
    public ImmutableArray<Plane<double>> Shape => polygons.Select(x => x.Surface.Plane).Distinct().ToImmutableArray();


    private static Polygon? Clip(Polygon poly, List<Polygon> piggyback, int i, bool keepShared, ImmutableArray<Plane<double>> shape)
    {
        var plane = shape[i];
        var classification = poly.Classify(plane);
        switch (classification)
        {
            case PlaneSide.CoplanarBack:
            case PlaneSide.Back:
                if(i + 1 >= shape.Length)
                    return null;

                return Clip(poly, piggyback, i + 1, keepShared, shape);

            case PlaneSide.Front:
            case PlaneSide.CoplanarFront:
                return poly;
            case PlaneSide.Coplanar:
                Console.WriteLine($"poly {poly.Surface.Plane} a {plane}");
                
                if(Vector3D.Dot(plane.Normal, poly.Surface.Plane.Normal) > 0)
                    return poly;
                
                if (i + 1 >= shape.Length)
                    return null;

                return Clip(poly, piggyback, i + 1, keepShared, shape);
            case PlaneSide.Spanning:
                var (back, front) = poly.Split(plane);

                Debug.Assert(back != null);
                Debug.Assert(front != null);

                if(i + 1 >= shape.Length)
                {
                    return front;
                }

                var clipAgain = Clip(back, piggyback, i + 1, keepShared, shape);

                // totally inside brush
                if (clipAgain == null)
                    return front;

                //ends up coplanar somewhere
                if (clipAgain == back)
                    return poly;

                piggyback.Add(clipAgain);
                return front;
        }

        return null;
    }


    private static List<Polygon> DeleteInsideShape(bool keepShared, List<Polygon> polygons, ImmutableArray<Plane<double>> shape)
    {
        var res = new List<Polygon>();
        
        foreach(var poly in polygons)
        {
            var i = 0;
            var o = Clip(poly, res, i, keepShared, shape);
            if (o != null)
            {
                res.Add(o); 
            }
        }

        return res;
    }

    public static void MergePolyhedrals(List<Polyhedral> polyhedrals)
    {
        var res = new List<Polyhedral>();

        foreach(var polyhedral in polyhedrals)
        {
            var keepShared = false;
            var polygons = new List<Polygon>(polyhedral.Polygons);
            foreach(var other in polyhedrals)
            {
                if(polyhedral == other)
                {
                    keepShared = true;
                    continue;
                }
                var shape = other.Shape;

                if (polyhedral.Bounds.Contains(other.Bounds))
                    continue;

                var outside = DeleteInsideShape(keepShared, polygons, shape);
                polygons = outside;
            }
            if(polygons.Count > 0)
            {
                res.Add(new Polyhedral(polygons.ToImmutableArray(), polyhedral.Bounds));
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

    internal static Polyhedral CreateFromBrush(MapBrush brush)
    {
        var planes = brush.Planes;
        var poly = new List<List<Vector3D<double>>>();
        var i = 0;
        for (i = 0; i < planes.Length; i++)
        {
            poly.Add(new());
        }

        var min = Vector3D<double>.One * double.MaxValue;
        var max = Vector3D<double>.One * double.MinValue;

        for (i = 0; i < planes.Length - 2; i++)
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
                        poly[i].Add(p);
                        poly[j].Add(p);
                        poly[k].Add(p);
                    }

                }
            }
        }
        var polys = new List<Polygon>();
        i = 0;
        foreach(var list in poly)
        {
            var p = Polygon.CreatePolygonFromPoints(list, planes[i++]);
            polys.Add(p);
        }
        return new Polyhedral(polys.ToImmutableArray(), new Box3D<double>(min, max));
    }
}
