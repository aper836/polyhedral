using Silk.NET.Maths;
using System.Collections.Immutable;
using System.Diagnostics;

namespace polyhedral;

internal class MapPlane(Plane<double> plane, Vector3D<double> tangent, Vector3D<double> bitangent)
{
    public Plane<double> Plane { get; set; } = plane;
    public Vector3D<double> Tangent => tangent;

    public Vector3D<double> Bitangent => bitangent;
}

internal class MapBrush(ImmutableArray<MapPlane> planes)
{
    public ImmutableArray<MapPlane> Planes => planes;
}

internal class MapEntity(Dictionary<string, string> attribs, ImmutableArray<MapBrush> brushes)
{
    public Dictionary<string, string> Attributes => attribs;

    public ImmutableArray<MapBrush> Brushes => brushes;
}

internal class Map(ImmutableArray<MapEntity> entities)
{
    public ImmutableArray<MapEntity> Entities => entities;
    public static Map Read(TextReader reader)
    {
        var str = reader.ReadLine();
        Debug.Assert(str != null);
        var entities = new List<MapEntity>();
        while (str != null)
        {
            if (str[0] == '{')
            {
                entities.Add(ReadEntity(reader));
            }
            str = reader.ReadLine();
        }
        return new Map(entities.ToImmutableArray());
    }
    private static MapEntity ReadEntity(TextReader reader)
    {
        var dictionary = new Dictionary<string, string>();
        var str = reader.ReadLine();
        var brushes = new List<MapBrush>();

        while (str != null && str[0] != '}')
        {
            if (str[0] == '{')
            {
                brushes.Add(ReadBrush(reader));
            }
            else if(str[0] == '"')
            {
                str = str.Replace("\"", "");
                var keyvalue = str.Split(" ");
                Debug.Assert(keyvalue.Length == 2);
                dictionary[keyvalue[0]] = keyvalue[1];
            }
            str = reader.ReadLine();
        }
        return new MapEntity(dictionary, brushes.ToImmutableArray());
    }
    private static Vector3D<double> ReadVec3(ref int index, string[] parts)
    {
        double x = 0, y = 0, z = 0;
        if (parts[index++] == "(")
        {
            x = double.Parse(parts[index++]);
            y = double.Parse(parts[index++]);
            z = double.Parse(parts[index++]);
        }
        index++;

        return new Vector3D<double>(x, y, z);
    }
    private static Vector3D<double> ReadVec4(ref int index, string[] parts)
    {
        double x = 0, y = 0, z = 0, w = 0;
        if (parts[index++] == "[")
        {
            x = double.Parse(parts[index++]);
            y = double.Parse(parts[index++]);
            z = double.Parse(parts[index++]);
            w = double.Parse(parts[index++]);
        }
        index++;

        return new Vector3D<double>(x, y, z);

    }

    private static MapBrush ReadBrush(TextReader reader)
    {
        var str = reader.ReadLine();
        var planes = new List<MapPlane>();
        while (str != null && str[0] != '}')
        {
            var parts = str.Split(" ");
            var index = 0;
            
            var first = ReadVec3(ref index, parts);
            var second = ReadVec3(ref index, parts);
            var third = ReadVec3(ref index, parts);

            var plane = Plane.CreateFromVertices(first, second, third);
            plane = Plane.Normalize(plane);

            index++;

            var tangent = ReadVec4(ref index, parts);
            var bitangent = ReadVec4(ref index, parts);

            planes.Add(new MapPlane(plane, tangent, bitangent));

            str = reader.ReadLine();
        }
        return new MapBrush(planes.ToImmutableArray());
    }
}

