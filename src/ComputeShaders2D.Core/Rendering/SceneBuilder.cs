using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using ComputeShaders2D.Core.Geometry;

namespace ComputeShaders2D.Core.Rendering;

internal sealed record ShapeInstance(
    Vector2[] Vertices,
    RgbaColor Color,
    FillRule Rule,
    IReadOnlyList<int> ClipRefs,
    IReadOnlyList<int> MaskRefs,
    float Opacity);

internal sealed record ClipInstance(Vector2[] Vertices, FillRule Rule);
internal sealed record MaskInstance(Vector2[] Vertices, FillRule Rule, float Alpha);

public sealed class SceneBuilder
{
    private readonly List<ShapeInstance> _shapes = new();
    private readonly List<ClipInstance> _clips = new();
    private readonly List<MaskInstance> _masks = new();
    private readonly Stack<List<int>> _clipStack = new();
    private readonly Stack<List<int>> _maskStack = new();
    private readonly Stack<float> _opacityStack = new();
    private readonly Random _random;

    public SceneBuilder(int width, int height, int tileSize = 64, int supersample = 2, int seed = 1234)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        Supersample = supersample;
        _random = new Random(seed);
        _opacityStack.Push(1f);
    }

    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }
    public int Supersample { get; }
    public double TimeSeconds { get; set; }
    public double DeltaSeconds { get; set; }
    public ulong Frame { get; set; }

    public void Fill(PathBuilder path, RgbaColor color, FillRule? rule = null)
    {
        var effectiveRule = rule ?? FillRule.EvenOdd;
        foreach (var polygon in path.Flatten())
        {
            if (polygon.Length < 3) continue;
            var verts = EnsureClosed(polygon);
            _shapes.Add(new ShapeInstance(
                verts,
                color,
                effectiveRule,
                GetActiveClipRefs(),
                GetActiveMaskRefs(),
                CurrentOpacity()));
        }
    }

    public void Stroke(PathBuilder path, float width, RgbaColor color, StrokeStyle? style = null)
    {
        var strokeStyle = style ?? StrokeStyle.Default;
        foreach (var poly in path.Flatten())
        {
            if (poly.Length < 2) continue;
            foreach (var strokePoly in StrokeRasterizer.BuildStroke(poly, width, strokeStyle))
            {
                _shapes.Add(new ShapeInstance(
                    strokePoly,
                    color,
                    FillRule.EvenOdd,
                    GetActiveClipRefs(),
                    GetActiveMaskRefs(),
                    CurrentOpacity()));
            }
        }
    }

    public List<Vector2> CreateRandomPolyline(int count)
    {
        var pts = new List<Vector2>(count);
        var position = new Vector2(Width * 0.5f, Height * 0.5f);
        var step = MathF.Min(Width, Height) * 0.05f;
        for (var i = 0; i < count; i++)
        {
            var delta = new Vector2(
                ((float)_random.NextDouble() - 0.5f) * step * 2f,
                ((float)_random.NextDouble() - 0.5f) * step * 2f);
            position += delta;
            position = Vector2.Clamp(position, Vector2.Zero + new Vector2(20), new Vector2(Width - 20, Height - 20));
            pts.Add(position);
        }

        return pts;
    }

    public void PushClip(PathBuilder path, FillRule rule = FillRule.EvenOdd)
    {
        var ids = new List<int>();
        foreach (var poly in path.Flatten())
        {
            var closed = EnsureClosed(poly);
            var id = _clips.Count;
            _clips.Add(new ClipInstance(closed, rule));
            ids.Add(id);
        }
        _clipStack.Push(ids);
    }

    public void PopClip()
    {
        if (_clipStack.Count == 0)
            throw new InvalidOperationException("Clip stack underflow");
        _clipStack.Pop();
    }

    public void PushOpacity(float value)
    {
        var clamped = Math.Clamp(value, 0f, 1f);
        _opacityStack.Push(clamped);
    }

    public void PopOpacity()
    {
        if (_opacityStack.Count <= 1)
            throw new InvalidOperationException("Opacity stack underflow");
        _opacityStack.Pop();
    }

    public void PushOpacityMask(PathBuilder path, float alpha = 1f, FillRule rule = FillRule.EvenOdd)
    {
        var ids = new List<int>();
        foreach (var poly in path.Flatten())
        {
            var closed = EnsureClosed(poly);
            var id = _masks.Count;
            _masks.Add(new MaskInstance(closed, rule, Math.Clamp(alpha, 0f, 1f)));
            ids.Add(id);
        }
        _maskStack.Push(ids);
    }

    public void PopOpacityMask()
    {
        if (_maskStack.Count == 0)
            throw new InvalidOperationException("Mask stack underflow");
        _maskStack.Pop();
    }

    public PackedScene Build()
    {
        var vertexList = new List<Vector2>();
        var clipVertexList = new List<Vector2>();
        var maskVertexList = new List<Vector2>();

        var shapes = new ShapeGpu[_shapes.Count];
        var clips = new ClipGpu[_clips.Count];
        var masks = new MaskGpu[_masks.Count];
        var clipRefs = new List<uint>();
        var maskRefs = new List<uint>();

        for (var i = 0; i < _shapes.Count; i++)
        {
            var shape = _shapes[i];
            var start = vertexList.Count;
            vertexList.AddRange(shape.Vertices);
            var color = shape.Color.ToPremultipliedVector();

            shapes[i] = new ShapeGpu
            {
                VStart = (uint)start,
                VCount = (uint)shape.Vertices.Length,
                Rule = shape.Rule,
                ColorR = color.X,
                ColorG = color.Y,
                ColorB = color.Z,
                ColorA = color.W,
                ClipStart = (uint)clipRefs.Count,
                ClipCount = (uint)shape.ClipRefs.Count,
                MaskStart = (uint)maskRefs.Count,
                MaskCount = (uint)shape.MaskRefs.Count,
                Opacity = shape.Opacity
            };

            foreach (var clipRef in shape.ClipRefs)
                clipRefs.Add((uint)clipRef);
            foreach (var maskRef in shape.MaskRefs)
                maskRefs.Add((uint)maskRef);
        }

        for (var i = 0; i < _clips.Count; i++)
        {
            var clip = _clips[i];
            var start = clipVertexList.Count + vertexList.Count; // after shape verts
            clipVertexList.AddRange(clip.Vertices);
            clips[i] = new ClipGpu
            {
                VStart = (uint)start,
                VCount = (uint)clip.Vertices.Length,
                Rule = clip.Rule
            };
        }

        for (var i = 0; i < _masks.Count; i++)
        {
            var mask = _masks[i];
            var start = maskVertexList.Count + vertexList.Count + clipVertexList.Count;
            maskVertexList.AddRange(mask.Vertices);
            masks[i] = new MaskGpu
            {
                VStart = (uint)start,
                VCount = (uint)mask.Vertices.Length,
                Rule = mask.Rule,
                Alpha = mask.Alpha
            };
        }

        var allVertices = vertexList
            .Concat(clipVertexList)
            .Concat(maskVertexList)
            .ToArray();

        var vertexFloats = new float[allVertices.Length * 2];
        for (var i = 0; i < allVertices.Length; i++)
        {
            vertexFloats[i * 2] = allVertices[i].X;
            vertexFloats[i * 2 + 1] = allVertices[i].Y;
        }

        var tileData = TileBinner.Build(shapes, vertexFloats, Width, Height, TileSize);

        return new PackedScene
        {
            Shapes = shapes,
            Clips = clips,
            Masks = masks,
            Vertices = vertexFloats,
            ClipRefs = clipRefs.ToArray(),
            MaskRefs = maskRefs.ToArray(),
            TileOffsetCounts = tileData.TileOffsetCounts,
            TileShapeIndices = tileData.TileShapeIndices,
            Uniforms = new UniformsGpu
            {
                CanvasW = (uint)Width,
                CanvasH = (uint)Height,
                TileSize = (uint)TileSize,
                TilesX = (uint)tileData.TilesX,
                Supersample = (uint)Supersample
            },
            TimeSeconds = TimeSeconds,
            DeltaSeconds = DeltaSeconds,
            Frame = Frame
        };
    }

    private float CurrentOpacity()
    {
        float result = 1f;
        foreach (var value in _opacityStack)
            result *= value;
        return result;
    }

    private IReadOnlyList<int> GetActiveClipRefs()
    {
        if (_clipStack.Count == 0) return Array.Empty<int>();
        return _clipStack.SelectMany(x => x).ToArray();
    }

    private IReadOnlyList<int> GetActiveMaskRefs()
    {
        if (_maskStack.Count == 0) return Array.Empty<int>();
        return _maskStack.SelectMany(x => x).ToArray();
    }

    private static Vector2[] EnsureClosed(IReadOnlyList<Vector2> poly)
    {
        if (poly.Count < 2) return poly.ToArray();
        var first = poly[0];
        var last = poly[^1];
        if (Vector2Extensions.NearlyEquals(first, last))
            return poly.ToArray();

        var list = new List<Vector2>(poly);
        list.Add(first);
        return list.ToArray();
    }
}
