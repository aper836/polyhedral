using Silk.NET.Maths;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Formats.Tar;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace polyhedral;

public interface IBinarySpacePartition
{
    public bool IsLeaf { get;  }
}
internal class Leaf(ImmutableArray<Polygon> volume) : IBinarySpacePartition
{
    public bool IsLeaf => true;

    public ImmutableArray<Polygon> Volume => volume;

    public bool IsSolid => Volume.Length == 0;
}

internal class Node : IBinarySpacePartition
{
    public Plane<double> Plane { get; set; }

    public IBinarySpacePartition Back { get; set; }
    public IBinarySpacePartition Front { get; set; }

    public bool IsLeaf => false;

    internal Node(Plane<double> plane, IBinarySpacePartition back, IBinarySpacePartition front)
    {
        Plane = plane;
        Back = back;
        Front = front;
    }
}
internal static class BSP
{
    internal class BSPPolygon(Polygon polygon, bool used)
    {
        public bool Used { get; set; } = used;
        public Polygon Polygon => polygon;

        public (BSPPolygon, BSPPolygon) Split(Plane<double> plane)
        {
            var (back, front) = Polygon.Split(plane);

            return (new BSPPolygon(back, Used), new BSPPolygon(front, Used));
        }
    }
    internal static Node BuildTreeRecursive(List<BSPPolygon> polygons)
    {
        var poly = polygons.Where(x => !x.Used).FirstOrDefault();
        Debug.Assert(poly != default);
        poly.Used = true;
        var plane = poly.Polygon.Surface.Plane;

        var front = new List<BSPPolygon>();
        var back = new List<BSPPolygon>();
        foreach (var polygon in polygons)
        {
            switch (polygon.Polygon.Classify(plane))
            {
                case PlaneSide.Front:
                case PlaneSide.CoplanarFront:
                    front.Add(polygon);
                    break;
                case PlaneSide.Back:
                case PlaneSide.CoplanarBack:
                    back.Add(polygon);
                    break;
                case PlaneSide.Coplanar:
                    if (Vector3D.Dot(plane.Normal, polygon.Polygon.Surface.Plane.Normal) > 0)
                        front.Add(polygon);
                    else
                        back.Add(polygon);
                    break;
                case PlaneSide.Spanning:
                    var (b, f) = polygon.Split(plane);
                    front.Add(f);
                    back.Add(b);
                    break;
                    
            }
        }


        IBinarySpacePartition backNode;
        IBinarySpacePartition frontNode;

        if (back.Count == 0)
        {
            backNode = new Leaf(ImmutableArray<Polygon>.Empty);
        }
        else
        {
            backNode = BuildTreeRecursive(back);
        }
        if (front.All(x => x.Used))
        {
            frontNode = new Leaf(front.Select(x => x.Polygon).ToImmutableArray());
        }
        else
        {
            frontNode = BuildTreeRecursive(front);
        }

        return new Node(plane, backNode, frontNode);
    }
    public static Node BuildTree(List<Polygon> polygons)
    {
        var candidates = polygons.Select(x => new BSPPolygon(x, false)).ToList();
        return BuildTreeRecursive(candidates);
    }

    public static void DebugPolygons(IBinarySpacePartition current, List<List<Polygon>> polygons)
    {
        if(current is Node node)
        {
            DebugPolygons(node.Back, polygons);
            DebugPolygons(node.Front, polygons);
        }
        if(current is Leaf leaf)
        {
            if (!leaf.IsSolid)
            {
                polygons.Add(leaf.Volume.ToList());
            }      
        }
    }
    const double BOUNDS_MAX = 1024;
    public static List<Polygon> BuildFillCell(uint size)
    {
        var region = new List<Polygon>();
        for (var i = 0; i < 3; i++)
        {
            double[] area = [0, 0, 0];
            for (var j = -1; j < 2; j++)
            {
                if (j == 0)
                    continue;

                area[i] = j;

                double dist = size;
                var normal = new Vector3D<double>(area[0], area[1], area[2]);
                var plane = new Plane<double>(normal, -dist);
                region.Add(Polygon.Create(plane, BOUNDS_MAX));
            }
        }
        return region;
    }
    private static void FixConvexCell(List<Polygon> bounds, Plane<double> plane)
    {
        var poly = Polygon.Create(plane, BOUNDS_MAX);
        foreach (var p in bounds)
        {
            switch (poly.Classify(p.Surface.Plane))
            {
                case PlaneSide.Spanning:
                    var (b, f) = poly.Split(p.Surface.Plane);
                    poly = f;
                    break;
            }
        }
        bounds.Add(poly);
    }
    private static void SplitCellUntilPolygon(Polygon polygon, List<Polygon> bounds, List<List<Polygon>> fillerSet, IBinarySpacePartition current)
    {

        if(current is Leaf l)
        {
            Console.WriteLine($"{bounds.Count}");
            if (!l.IsSolid)
                fillerSet.Add(bounds);

            return;
        }

        var node = current as Node;
        Debug.Assert(node != null);
        var plane = node.Plane;
        var splits = false;


        var backCell = new List<Polygon>();
        var frontCell = new List<Polygon>();
        
        foreach(var p in bounds)
        {
            switch (p.Classify(plane))
            {
                case PlaneSide.CoplanarFront:
                case PlaneSide.Front:
                    frontCell.Add(p);
                    break;
                case PlaneSide.Back:
                case PlaneSide.CoplanarBack:
                    backCell.Add(p);
                    break;
                case PlaneSide.Coplanar:
                    frontCell.Add(p);
                    backCell.Add(p);
                    break;
                case PlaneSide.Spanning:
                    var (back, front) = p.Split(plane);
                    frontCell.Add(front);
                    backCell.Add(back);
                    splits = true;
                    break;
            }
        }

        if (splits)
        {
            FixConvexCell(frontCell, plane);
            var backPlane = new Plane<double>(plane.Normal * -1, plane.Distance * -1);
            FixConvexCell(backCell, backPlane);
        }

        switch (polygon.Classify(node.Plane))
        {
            case PlaneSide.CoplanarBack:
            case PlaneSide.Back:
                SplitCellUntilPolygon(polygon, backCell, fillerSet, node.Back);
                break;

            case PlaneSide.Front:
            case PlaneSide.CoplanarFront:
                SplitCellUntilPolygon(polygon, frontCell, fillerSet, node.Front);
                break;

            case PlaneSide.Coplanar:
                if (Vector3D.Dot(plane.Normal, polygon.Surface.Plane.Normal) > 0)
                    SplitCellUntilPolygon(polygon, frontCell, fillerSet, node.Front);
                else
                    SplitCellUntilPolygon(polygon, backCell, fillerSet, node.Back);
                break;
            case PlaneSide.Spanning:
                var (b, f) = polygon.Split(plane);
                SplitCellUntilPolygon(b, backCell, fillerSet, node.Back);
                SplitCellUntilPolygon(f, frontCell, fillerSet, node.Front);
                break;
        }


    }
    public static List<Polygon> BuildCells(List<Polygon> polygons, Node root)
    {
        var bounds = BuildFillCell(1024);
        var fillerSet = new List<List<Polygon>>();
        foreach (var polygon in polygons) 
        {
            SplitCellUntilPolygon(polygon, bounds, fillerSet, root);
        }
        Console.WriteLine($"Cells filled: {fillerSet.Count}");
        return fillerSet.SelectMany(x => x).ToList();
    }

    private static void PrintJsonRecursive(JsonNode json, Node node)
    {
        
    }
    public static void PrintJson(Node root)
    {
        var file = File.Open("./bsptree.json", FileMode.Create);
        var stream = new StreamWriter(file);
        var obj = new JsonObject();
        PrintJsonRecursive(obj, root);
        stream.Write(obj.ToString());
        stream.Flush();
        stream.Close();
        file.Close();
    }
}
