using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using TrippyGL;

namespace polyhedral;

public class App
{
    private IWindow _window;
    private IInputContext? _input;
    private GraphicsDevice? _graphicsDevice;
    private InputManager3D? _inputManager;

    private SimpleShaderProgram? _defaultShaderProgram;
    private VertexBuffer<VertexColor>? _mapVertexBuffer;


    public App(IWindow window)
    {
        _window = window;
        _window.Load += Window_Load;
        _window.Render += Render;
        _window.Update += Update;
    }
    public void Render(double dt)
    {
        if (_window.IsClosing)
            return;

        Debug.Assert(_graphicsDevice != null);

        Debug.Assert(_mapVertexBuffer != null);
        Debug.Assert(_defaultShaderProgram != null);

        _graphicsDevice.ClearColor = new Vector4(0, 0, 0, 1);
        _graphicsDevice.Clear(ClearBuffers.Color 
            | ClearBuffers.Depth);
        
        _graphicsDevice.VertexArray = _mapVertexBuffer;
        _graphicsDevice.ShaderProgram = _defaultShaderProgram;


        Debug.Assert(_inputManager != null);
        _defaultShaderProgram.Projection = Matrix4x4.CreatePerspectiveFieldOfView(90 * (MathF.PI / 180), _window.Size.X / _window.Size.Y, 0.1f, 1000f);
        _defaultShaderProgram.View = Matrix4x4.CreateLookAt(_inputManager.CameraPosition, _inputManager.CameraPosition + _inputManager.CalculateForwardVector(), Vector3.UnitZ);
        _defaultShaderProgram.World = Matrix4x4.CreateScale(0.5f);

        _graphicsDevice.DrawArrays(PrimitiveType.Triangles,
                                   0,
                                   _mapVertexBuffer.Value.StorageLength);

    }
    public void Update(double dt)
    {
        if (_window.IsClosing) return;

        Debug.Assert(_inputManager != null);
        _inputManager.Update((float)dt);
    }

    public static App CreateApp()
    {
        var monitor = Silk.NET.Windowing.Monitor.GetMainMonitor(null);
        var options = new WindowOptions
        {
            API = GraphicsAPI.Default,
            IsVisible = true,
            VSync = true,
            Title = "polyhedral",
            ShouldSwapAutomatically = true,
            FramesPerSecond = 60,
            UpdatesPerSecond = 60,
            Size = new Vector2D<int>(800, 600),
            Position = monitor.Bounds.Origin + new Vector2D<int>(50)
        };
        var window = Window.Create(options);
        return new App(window);
    }

    private void Window_Load()
    {
        _window.Center();
        Console.WriteLine("Loading window...");
        _input = _window.CreateInput();
        _graphicsDevice = new GraphicsDevice(Silk.NET.OpenGL.GL.GetApi(_window.GLContext))
        {
            DebugMessagingEnabled = true
        };
        _graphicsDevice.ShaderCompiled += GraphicsDevice_ShaderCompiled;
        _graphicsDevice.DebugMessageReceived += OnDebugMessage;

        Console.WriteLine(string.Concat("GL Version: ", _graphicsDevice.GLMajorVersion, ".", _graphicsDevice.GLMinorVersion));
        Console.WriteLine("GL Version String: " + _graphicsDevice.GLVersion);
        Console.WriteLine("GL Vendor: " + _graphicsDevice.GLVendor);
        Console.WriteLine("GL Renderer: " + _graphicsDevice.GLRenderer);
        Console.WriteLine("GL ShadingLanguageVersion: " + _graphicsDevice.GLShadingLanguageVersion);
        Console.WriteLine("GL TextureUnits: " + _graphicsDevice.MaxTextureImageUnits);
        Console.WriteLine("GL MaxTextureSize: " + _graphicsDevice.MaxTextureSize);
        Console.WriteLine("GL MaxSamples: " + _graphicsDevice.MaxSamples);

        _graphicsDevice.SetViewport(0, 0, (uint)_window.Size.X, (uint)_window.Size.Y);
        _graphicsDevice.DepthTestingEnabled = true;
        _graphicsDevice.FaceCullingEnabled = true;
        _inputManager = new InputManager3D(_input);
        _inputManager.CameraMoveSpeed = 18;

        Start();
    }
    private static void OnDebugMessage(TrippyGL.DebugSource debugSource, TrippyGL.DebugType debugType, int messageId, TrippyGL.DebugSeverity debugSeverity, string message)
    {
        if (messageId != 131185 && messageId != 131186)
            Console.WriteLine(string.Concat("Debug message: source=", debugSource.ToString(), " type=", debugType.ToString(), " id=", messageId.ToString(), " severity=", debugSeverity.ToString(), " message=\"", message, "\""));
    }
    private void GraphicsDevice_ShaderCompiled(GraphicsDevice sender, in ShaderProgramBuilder programBuilder, bool success)
    {
        bool hasVsLog = !string.IsNullOrEmpty(programBuilder.VertexShaderLog);
        bool hasGsLog = !string.IsNullOrEmpty(programBuilder.GeometryShaderLog);
        bool hasFsLog = !string.IsNullOrEmpty(programBuilder.FragmentShaderLog);
        bool hasProgramLog = !string.IsNullOrEmpty(programBuilder.ProgramLog);
        bool printLogs = false;

        if (success)
        {
            if (hasVsLog || hasGsLog || hasFsLog || hasProgramLog)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Shader compiled with possible warnings:");
                printLogs = true;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Shader compiled succesfully.");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Shader compilation error:");
            printLogs = true;
        }

        if (printLogs)
        {
            if (hasVsLog)
            {
                Console.WriteLine("VertexShader log:");
                Console.WriteLine(programBuilder.VertexShaderLog);
            }

            if (hasGsLog)
            {
                Console.WriteLine("GeometryShader log:");
                Console.WriteLine(programBuilder.GeometryShaderLog);
            }

            if (hasFsLog)
            {
                Console.WriteLine("FragmentShader log:");
                Console.WriteLine(programBuilder.FragmentShaderLog);
            }

            if (hasProgramLog)
            {
                Console.WriteLine("Program log:");
                Console.WriteLine(programBuilder.ProgramLog);
            }
        }

        Console.ResetColor();
    }

    

    private void Start()
    {
        var file = File.OpenRead("./unnamed.map");
        var reader = new StreamReader(file);
        var map = Map.Read(reader);


        List<Brush> list = new();
        
        foreach(var brush in map.Entities[0].Brushes)
        {
            list.Add(Brush.CreateFrom(brush));
        }
        
        Brush.UnionBrushes(list);

        reader.Close();
        file.Close();

        var batcher = new PrimitiveBatcher<VertexColor>();
        batcher.EnsureTriangleSpace(2048);
        var polys = list.SelectMany(x => x.GetPolygons()).ToList();
        
        var node = BSP.BuildTree(polys);
        BSP.PrintJson(node);
        Debug.Assert(_graphicsDevice !=  null);
        var end = new List<List<Polygon>>();
        BSP.DebugPolygons(node, end);
        var res = end.SelectMany(x => x).ToList();

        var cellM = BSP.GenerateCells(res, node);

        /*
        foreach (var polyhedra in end)
        {
            var color0 = new Color4b((byte)Random.Shared.Next(255), (byte)Random.Shared.Next(255), (byte)Random.Shared.Next(255));
            foreach(var poly in polyhedra)
            {
                var triangles = poly.Triangulate();
                foreach (var tri in triangles)
                {
                    var first = new VertexColor(tri.Item1.ToSystem(), color0);
                    var second = new VertexColor(tri.Item2.ToSystem(), color0);
                    var third = new VertexColor(tri.Item3.ToSystem(), color0);
                    batcher.AddTriangle(first, second, third);
                }
            }
            
        }
        */
        foreach (var poly in polys)
        {
            var color = new Color4b((byte)Random.Shared.Next(255), (byte)Random.Shared.Next(255), (byte)Random.Shared.Next(255));
            var triangles = poly.Triangulate();
            foreach (var tri in triangles)
            {
                var first = new VertexColor(tri.Item1.ToSystem(), color);
                var second = new VertexColor(tri.Item2.ToSystem(), color);
                var third = new VertexColor(tri.Item3.ToSystem(), color);

                batcher.AddTriangle(first, second, third);
            }
        }

        batcher.TrimTriangles();

        _mapVertexBuffer = new VertexBuffer<VertexColor>(_graphicsDevice, batcher.TriangleVertices, BufferUsage.StaticDraw);
        _defaultShaderProgram = SimpleShaderProgram.Create<VertexColor>(_graphicsDevice);
    }
    public void Run()
    {
        _window.Run();
    }


}
