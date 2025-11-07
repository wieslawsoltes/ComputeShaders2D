using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using ComputeShaders2D.Core.Geometry;

namespace ComputeShaders2D.Core.Rendering;

/// <summary>
/// Public entry point exposed to scene scripts/tests.
/// It mirrors the JS playground API while building a PackedScene for GPU backends.
/// </summary>
public sealed class VectorApi
{
    private readonly SceneBuilder _builder;
    private FillRule _fillRule = FillRule.EvenOdd;

    public VectorApi(int width, int height, int tileSize = 64, int supersample = 2)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        Supersample = supersample;
        _builder = new SceneBuilder(width, height, tileSize, supersample);
    }

    public int Width { get; }
    public int Height { get; }
    public int TileSize { get; }
    public int Supersample { get; }

    public double TimeSeconds
    {
        get => _builder.TimeSeconds;
        set => _builder.TimeSeconds = value;
    }

    public double DeltaSeconds
    {
        get => _builder.DeltaSeconds;
        set => _builder.DeltaSeconds = value;
    }

    public ulong Frame
    {
        get => _builder.Frame;
        set => _builder.Frame = value;
    }

    public FillRule DefaultFillRule
    {
        get => _fillRule;
        set => _fillRule = value;
    }

    public PathBuilder Path() => new();

    public RgbaColor Color(byte r, byte g, byte b, byte a = 255) => RgbaColor.FromBytes(r, g, b, a);

    public Task<IFont> LoadFontAsync(ReadOnlyMemory<byte> fontBytes) => FontLoader.LoadAsync(fontBytes);

    public PathBuilder TextPath(IFont font, string text, float x, float y, float size, TextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(font);
        var resolved = options ?? TextOptions.Default;
        var builder = TextOutliner.LayoutText(font, text ?? string.Empty, x, y, size, resolved);
        return builder;
    }

    public void FillText(IFont font, string text, float x, float y, float size, RgbaColor color, TextOptions? options = null)
        => FillPath(TextPath(font, text, x, y, size, options), color, _fillRule);

    public void StrokeText(IFont font, string text, float x, float y, float size, float width, RgbaColor color, TextOptions? options = null, StrokeStyle? style = null)
        => StrokePath(TextPath(font, text, x, y, size, options), width, color, style);

    public void FillPath(PathBuilder path, RgbaColor color, FillRule? rule = null)
        => _builder.Fill(path, color, rule ?? _fillRule);

    public void StrokePath(PathBuilder path, float width, RgbaColor color, StrokeStyle? style = null)
        => _builder.Stroke(path, width, color, style);

    public void PushClip(PathBuilder path, FillRule rule = FillRule.EvenOdd)
        => _builder.PushClip(path, rule);

    public void PopClip() => _builder.PopClip();

    public void PushOpacity(float alpha) => _builder.PushOpacity(alpha);

    public void PopOpacity() => _builder.PopOpacity();

    public void PushOpacityMask(PathBuilder path, float alpha = 1f, FillRule rule = FillRule.EvenOdd)
        => _builder.PushOpacityMask(path, alpha, rule);

    public void PopOpacityMask() => _builder.PopOpacityMask();

    public PathBuilder SvgPath(string pathData)
        => SvgPathParser.Parse(pathData);

    public void FillSvg(string pathData, RgbaColor color, FillRule? rule = null)
        => FillPath(SvgPath(pathData), color, rule ?? _fillRule);

    public void StrokeSvg(string pathData, float width, RgbaColor color, StrokeStyle? style = null)
        => StrokePath(SvgPath(pathData), width, color, style);

    public float[] SvgToPathVerts(string pathData)
    {
        var path = SvgPath(pathData);
        var flattened = path.Flatten();
        var list = new List<float>();
        foreach (var poly in flattened)
        {
            foreach (var point in poly)
            {
                list.Add(point.X);
                list.Add(point.Y);
            }
        }
        return list.ToArray();
    }

    public IReadOnlyList<Vector2> RandomPolyline(int count) => _builder.CreateRandomPolyline(count);

    public PackedScene BuildScene() => _builder.Build();

    public Vector2[] Star(float cx, float cy, float outerRadius, float innerRadius, int points)
    {
        var list = new Vector2[points * 2];
        var step = MathF.PI / points;
        for (var i = 0; i < points * 2; i++)
        {
            var radius = (i % 2 == 0) ? outerRadius : innerRadius;
            var angle = i * step - MathF.PI / 2f;
            list[i] = new Vector2(
                cx + MathF.Cos(angle) * radius,
                cy + MathF.Sin(angle) * radius);
        }
        return list;
    }
}
