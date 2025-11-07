using ComputeShaders2D.AvaloniaApp.Models;

namespace ComputeShaders2D.AvaloniaApp.Rendering;

internal static class SampleScripts
{
    public const string DefaultSample = """
var W = (float)api.Width;
var H = (float)api.Height;
var k = MathF.Min(W, H);

var yellow = api.Color(250, 245, 140, 255);
var sky = api.Color(88, 156, 255, 220);
var coral = api.Color(255, 120, 88, 190);

var star = api.Path().Poly(api.Star(W * 0.5f, H * 0.35f, k * 0.26f, k * 0.11f, 7), true);
api.FillPath(star, sky);

var blob = api.Path()
    .Ellipse(W * 0.26f, H * 0.68f, k * 0.12f, k * 0.08f)
    .Transform(scaleX: 1.06f, scaleY: 1f, rotate: 0.08f)
    .ClosePath();
api.FillPath(blob, coral);

var randomPath = api.Path();
var points = api.RandomPolyline(220);
if (points.Count > 0)
{
    randomPath.MoveTo(points[0].X, points[0].Y);
    for (var i = 1; i < points.Count; i++)
    {
        randomPath.LineTo(points[i].X, points[i].Y);
    }
}
api.StrokePath(randomPath, Settings.StrokeWidth, yellow, new StrokeStyle(Settings.StrokeJoin, Settings.StrokeCap, Settings.MiterLimit));

var curve = api.Path()
    .MoveTo(W * 0.12f, H * 0.15f)
    .BezierTo(W * 0.28f, H * 0.06f, W * 0.42f, H * 0.28f, W * 0.52f, H * 0.15f)
    .QuadTo(W * 0.64f, H * 0.03f, W * 0.78f, H * 0.16f);
api.StrokePath(curve, 8, api.Color(160, 220, 255, 255), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));

if (Settings.UseSystemFont)
{
    var label = api.TextPath(Font, "Hello from GPU", W * 0.18f, H * 0.90f, k * 0.08f, TextOptions.Default);
    api.StrokePath(label, 3f, api.Color(18, 24, 42, 255), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
    api.FillPath(label, api.Color(245, 242, 235, 255));
}

if (Settings.ShowSvgOverlay)
{
    var scale = k * 0.18f / 64f;
    var logo = api.SvgPath(SvgPath).Transform(scaleX: scale, scaleY: scale, translateX: W * 0.62f, translateY: H * 0.58f);
    api.FillPath(logo, api.Color(16, 24, 42, 210));
    api.StrokePath(logo, 2f, api.Color(255, 255, 255, 210), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
}
""";

    public const string AnimationSample = """
var W = (float)api.Width;
var H = (float)api.Height;
var k = MathF.Min(W, H);
var t = (float)Context.TimeSeconds;

var yellow = api.Color(250, 245, 140, 255);
var sky = api.Color(88, 156, 255, 220);

var outer = 0.26f * k * (1f + 0.06f * MathF.Sin(t * 1.2f));
var inner = 0.11f * k;
var star = api.Path().Poly(api.Star(0, 0, outer, inner, 7), true)
    .Transform(translateX: W * 0.5f, translateY: H * 0.38f, rotate: t * 0.9f);
api.FillPath(star, sky);

var ribbon = api.Path();
var A = 0.32f * k;
var B = 0.18f * k;
var nPts = 340;
for (var i = 0; i <= nPts; i++)
{
    var u = i / (float)nPts;
    var x = W * 0.5f + MathF.Sin(t * 0.8f + u * MathF.Tau * 2f) * A;
    var y = H * 0.70f + MathF.Sin(t * 1.1f + u * MathF.Tau * 3f) * B;
    if (i == 0) ribbon.MoveTo(x, y); else ribbon.LineTo(x, y);
}
api.StrokePath(ribbon, Settings.StrokeWidth, yellow, new StrokeStyle(Settings.StrokeJoin, Settings.StrokeCap, Settings.MiterLimit));
""";

    public const string ClipSample = """
var W = (float)api.Width;
var H = (float)api.Height;
var t = (float)Context.TimeSeconds;
var k = MathF.Min(W, H);

var cyan = api.Color(120, 220, 255, 210);
var pink = api.Color(255, 120, 180, 220);
var gold = api.Color(252, 212, 98, 230);

var clipPath = api.Path().Rect(W * 0.18f, H * 0.14f, W * 0.64f, H * 0.64f);
api.PushClip(clipPath, api.DefaultFillRule);

for (var i = 0; i < 6; i++)
{
    var a = t * 0.5f + i * 0.4f;
    var outer = 0.26f * k * (0.65f + 0.35f * MathF.Sin(t * 0.9f + i));
    var inner = 0.11f * k * (0.65f + 0.35f * MathF.Cos(t * 0.7f + i));
    var cx = W * 0.5f + MathF.Cos(a) * k * 0.08f;
    var cy = H * 0.42f + MathF.Sin(a * 1.1f) * k * 0.06f;
    var star = api.Path().Poly(api.Star(0, 0, outer, inner, 7), true).Transform(translateX: cx, translateY: cy, rotate: a);
    api.FillPath(star, (i % 2 == 0) ? cyan : pink, FillRule.EvenOdd);
}

var lissajous = api.Path();
var pts = 300;
for (var i = 0; i <= pts; i++)
{
    var u = i / (float)pts;
    var x = W * 0.5f + MathF.Sin(t * 0.4f + u * MathF.Tau * 2f) * (0.34f * k);
    var y = H * 0.68f + MathF.Sin(t * 0.7f + u * MathF.Tau * 3f) * (0.18f * k);
    if (i == 0) lissajous.MoveTo(x, y); else lissajous.LineTo(x, y);
}
api.StrokePath(lissajous, 14, gold, new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
api.PopClip();
""";

    public const string MaskSample = """
var W = (float)api.Width;
var H = (float)api.Height;
var t = (float)Context.TimeSeconds;
var k = MathF.Min(W, H);

var yellow = api.Color(250, 245, 140, 255);
var blue = api.Color(100, 160, 255, 220);
var green = api.Color(120, 220, 150, 220);

var mask = api.Path().Ellipse(W * 0.5f, H * 0.55f, k * 0.25f, k * 0.10f);
api.PushOpacityMask(mask, 0.85f, api.DefaultFillRule);

for (var i = 0; i < 18; i++)
{
    var a = t * 0.6f + i * (MathF.Tau / 18f);
    var r = 20f + 12f * MathF.Sin(t * 1.3f + i);
    var cx = W * 0.5f + MathF.Cos(a) * k * 0.28f;
    var cy = H * 0.55f + MathF.Sin(a * 1.1f) * k * 0.18f;
    var ellipse = api.Path().Ellipse(cx, cy, r, r).ClosePath();
    api.FillPath(ellipse, (i % 2 == 0) ? blue : green);
}

api.PushOpacity(0.6f);
var ribbon = api.Path();
var n = 320;
for (var i = 0; i <= n; i++)
{
    var u = i / (float)n;
    var x = W * 0.5f + MathF.Sin(t * 0.8f + u * MathF.Tau * 3f) * (0.22f * k);
    var y = H * 0.55f + MathF.Sin(t * 1.2f + u * MathF.Tau * 4f) * (0.12f * k);
    if (i == 0) ribbon.MoveTo(x, y); else ribbon.LineTo(x, y);
}
api.StrokePath(ribbon, 10, yellow, new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
api.PopOpacity();
api.PopOpacityMask();
""";

    public static string GetSampleText(SceneSampleKind sample) => sample switch
    {
        SceneSampleKind.Animation => AnimationSample,
        SceneSampleKind.Clip => ClipSample,
        SceneSampleKind.Mask => MaskSample,
        _ => DefaultSample
    };
}
