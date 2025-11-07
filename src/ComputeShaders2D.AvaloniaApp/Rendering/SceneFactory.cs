using System;
using System.Collections.Generic;
using System.Numerics;
using ComputeShaders2D.AvaloniaApp.Models;
using ComputeShaders2D.Core.Rendering;

namespace ComputeShaders2D.AvaloniaApp.Rendering;

internal sealed class SceneFactory
{
    private readonly Random _random = new(12345);
    private List<Vector2> _randomPolyline = new();

    public PackedScene BuildScene(RenderFrameContext context, RenderSettings settings, SceneSampleKind sample, bool randomize, IFont? font)
    {
        if (randomize || _randomPolyline.Count == 0)
        {
            _randomPolyline = new List<Vector2>(GenerateRandomPolyline(settings.CanvasWidth, settings.CanvasHeight, 220));
        }

        var api = new VectorApi((int)context.Width, (int)context.Height, settings.TileSize, settings.Supersample)
        {
            DefaultFillRule = settings.FillRule,
            TimeSeconds = context.TimeSeconds,
            DeltaSeconds = context.DeltaSeconds,
            Frame = context.FrameIndex
        };

        var strokeStyle = new StrokeStyle(settings.StrokeJoin, settings.StrokeCap, settings.MiterLimit);

        switch (sample)
        {
            case SceneSampleKind.Animation:
                BuildAnimationScene(api, settings, strokeStyle);
                break;
            case SceneSampleKind.Clip:
                BuildClipScene(api, settings, strokeStyle);
                break;
            case SceneSampleKind.Mask:
                BuildMaskScene(api, settings, strokeStyle);
                break;
            default:
                BuildDefaultScene(api, settings, strokeStyle);
                break;
        }

        AddFontOverlay(api, settings, font);
        AddSvgOverlay(api, settings);

        return api.BuildScene();
    }

    private void BuildDefaultScene(VectorApi api, RenderSettings settings, StrokeStyle strokeStyle)
    {
        var k = Math.Min(api.Width, api.Height);
        var star = api.Path()
            .Poly(api.Star(api.Width * 0.5f, api.Height * 0.35f, k * 0.26f, k * 0.11f, 7), close: true);
        api.FillPath(star, api.Color(88, 156, 255, 220));

        var blob = api.Path()
            .Ellipse(api.Width * 0.26f, api.Height * 0.68f, k * 0.12f, k * 0.08f)
            .Transform(rotate: 0.08f, scaleX: 1.06f);
        api.FillPath(blob, api.Color(255, 120, 88, 190));

        var randomPath = api.Path();
        if (_randomPolyline.Count > 0)
        {
            randomPath.Poly(_randomPolyline);
        }
        api.StrokePath(randomPath, settings.StrokeWidth, api.Color(250, 245, 140, 255), strokeStyle);

        var curve = api.Path()
            .MoveTo(api.Width * 0.12f, api.Height * 0.15f)
            .BezierTo(api.Width * 0.28f, api.Height * 0.06f, api.Width * 0.42f, api.Height * 0.28f, api.Width * 0.52f, api.Height * 0.15f)
            .QuadTo(api.Width * 0.64f, api.Height * 0.03f, api.Width * 0.78f, api.Height * 0.16f);
        api.StrokePath(curve, 8, api.Color(160, 220, 255, 255), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
    }

    private void BuildAnimationScene(VectorApi api, RenderSettings settings, StrokeStyle strokeStyle)
    {
        var k = Math.Min(api.Width, api.Height);
        var time = (float)api.TimeSeconds;

        var outer = k * (0.22f + 0.04f * MathF.Sin(time * 1.2f));
        var inner = k * 0.1f;
        var starPath = api.Path().Poly(api.Star(api.Width * 0.5f, api.Height * 0.38f, outer, inner, 7), close: true);
        api.FillPath(starPath, api.Color(88, 156, 255, 220));

        var ribbon = api.Path();
        var points = 320;
        for (int i = 0; i <= points; i++)
        {
            var u = i / (float)points;
            var x = api.Width * 0.5f + MathF.Sin(time * 0.8f + u * MathF.Tau * 2f) * (k * 0.32f);
            var y = api.Height * 0.70f + MathF.Sin(time * 1.1f + u * MathF.Tau * 3f) * (k * 0.18f);
            if (i == 0) ribbon.MoveTo(x, y); else ribbon.LineTo(x, y);
        }
        api.StrokePath(ribbon, settings.StrokeWidth, api.Color(250, 245, 140, 255), strokeStyle);
    }

    private void BuildClipScene(VectorApi api, RenderSettings settings, StrokeStyle strokeStyle)
    {
        var k = Math.Min(api.Width, api.Height);
        var clipPath = api.Path().Rect(api.Width * 0.18f, api.Height * 0.14f, api.Width * 0.64f, api.Height * 0.64f);
        api.PushClip(clipPath, settings.FillRule);
        for (var i = 0; i < 6; i++)
        {
            var a = (float)api.TimeSeconds * 0.5f + i * 0.4f;
            var rOuter = 0.26f * k * (0.65f + 0.35f * MathF.Sin((float)api.TimeSeconds * 0.9f + i));
            var rInner = 0.11f * k * (0.65f + 0.35f * MathF.Cos((float)api.TimeSeconds * 0.7f + i));
            var cx = api.Width * 0.5f + MathF.Cos(a) * k * 0.08f;
            var cy = api.Height * 0.42f + MathF.Sin(a * 1.1f) * k * 0.06f;
            var path = api.Path().Poly(api.Star(cx, cy, rOuter, rInner, 7), close: true);
            var color = (i % 2 == 0) ? api.Color(120, 220, 255, 210) : api.Color(255, 120, 180, 220);
            api.FillPath(path, color, FillRule.EvenOdd);
        }

        var lissajous = api.Path();
        var pts = 300;
        for (var i = 0; i <= pts; i++)
        {
            var u = i / (float)pts;
            var x = api.Width * 0.5f + MathF.Sin((float)api.TimeSeconds * 0.4f + u * MathF.Tau * 2f) * (0.34f * k);
            var y = api.Height * 0.68f + MathF.Sin((float)api.TimeSeconds * 0.7f + u * MathF.Tau * 3f) * (0.18f * k);
            if (i == 0) lissajous.MoveTo(x, y); else lissajous.LineTo(x, y);
        }
        api.StrokePath(lissajous, 14, api.Color(252, 212, 98, 230), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
        api.PopClip();
    }

    private void BuildMaskScene(VectorApi api, RenderSettings settings, StrokeStyle strokeStyle)
    {
        var k = Math.Min(api.Width, api.Height);
        var mask = api.Path().Ellipse(api.Width * 0.5f, api.Height * 0.55f, k * 0.25f, k * 0.10f);
        api.PushOpacityMask(mask, 0.85f, settings.FillRule);
        for (var i = 0; i < 18; i++)
        {
            var a = (float)api.TimeSeconds * 0.6f + i * (2f * MathF.PI / 18f);
            var r = 20 + 12 * MathF.Sin((float)api.TimeSeconds * 1.3f + i);
            var cx = api.Width * 0.5f + MathF.Cos(a) * k * 0.28f;
            var cy = api.Height * 0.55f + MathF.Sin(a * 1.1f) * k * 0.18f;
            var ellipse = api.Path().Ellipse(cx, cy, r, r);
            var color = (i % 2 == 0) ? api.Color(100, 160, 255, 220) : api.Color(120, 220, 150, 220);
            api.FillPath(ellipse, color);
        }

        api.PushOpacity(0.6f);
        var ribbon = api.Path();
        var n = 320;
        for (var i = 0; i <= n; i++)
        {
            var u = i / (float)n;
            var x = api.Width * 0.5f + MathF.Sin((float)api.TimeSeconds * 0.8f + u * MathF.Tau * 3f) * (0.22f * k);
            var y = api.Height * 0.55f + MathF.Sin((float)api.TimeSeconds * 1.2f + u * MathF.Tau * 4f) * (0.12f * k);
            if (i == 0) ribbon.MoveTo(x, y); else ribbon.LineTo(x, y);
        }
        api.StrokePath(ribbon, 10, api.Color(250, 245, 140, 255), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
        api.PopOpacity();
        api.PopOpacityMask();
    }

    private IEnumerable<Vector2> GenerateRandomPolyline(int width, int height, int count)
    {
        var pts = new List<Vector2>(count);
        var pos = new Vector2(width * 0.15f, height * 0.25f);
        var step = Math.Min(width, height) * 0.03f;
        for (var i = 0; i < count; i++)
        {
            pos += new Vector2((float)(_random.NextDouble() - 0.5) * step * 2f,
                               (float)(_random.NextDouble() - 0.5) * step * 2f);
            pos = Vector2.Clamp(pos, new Vector2(20, 20), new Vector2(width - 20, height - 20));
            pts.Add(pos);
        }
        return pts;
    }

    private static void AddFontOverlay(VectorApi api, RenderSettings settings, IFont? font)
    {
        if (!settings.UseSystemFont || font is null)
            return;

        var size = MathF.Min(api.Width, api.Height) * 0.08f;
        var text = api.TextPath(font, "ComputeShaders2D", api.Width * 0.18f, api.Height * 0.90f, size, TextOptions.Default);
        api.StrokePath(text, 3f, api.Color(18, 24, 42, 255), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
        api.FillPath(text, api.Color(250, 245, 210, 255));
    }

    private static void AddSvgOverlay(VectorApi api, RenderSettings settings)
    {
        if (!settings.ShowSvgOverlay)
            return;

        var scale = MathF.Min(api.Width, api.Height) * 0.18f / 64f;
        var svg = api.SvgPath(SceneAssets.LogoPath)
            .Transform(scaleX: scale, scaleY: scale, translateX: api.Width * 0.62f, translateY: api.Height * 0.58f);

        api.FillPath(svg, api.Color(16, 24, 42, 210));
        api.StrokePath(svg, 2.2f, api.Color(255, 255, 255, 210), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
    }
}
