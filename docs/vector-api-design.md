# VectorApi & SceneBuilder Design (Task 2)

This fulfills Task 2 from `docs/dotnet-silk-gpu-plan.md` by defining the managed authoring surface that mirrors the JS playground and produces GPU-ready buffers.

## Goals
- Port the JS API 1:1 so existing samples (star stroke, clip/mask demos) translate directly into C#.
- Keep path flattening, stroking, clip/mask stacks, and random generators deterministic vs. the JS reference.
- Emit a `PackedScene` with buffers matching the WGSL expectations (Shape=64 bytes, Clip=16 bytes, Mask=32 bytes) for downstream GPU backends.

## Public API Surface
```csharp
public sealed class VectorApi
{
    public int Width { get; }
    public int Height { get; }

    // Time/animation controls (set from host)
    public double TimeSeconds  { get; set; }
    public double DeltaSeconds { get; set; }
    public ulong  Frame        { get; set; }

    // Authoring helpers
    public Rgba8 Color(byte r, byte g, byte b, byte a = 255);
    public PathBuilder Path();
    public float2[] Star(float cx, float cy, float rOuter, float rInner, int points = 5);
    public float2[] RandomPolyline(int count);

    // Scene commands (mirror JS)
    public void FillPath(PathBuilder path, Rgba8 color, FillRule rule = FillRule.EvenOdd);
    public void StrokePath(PathBuilder path, float width, Rgba8 color, StrokeStyle style);
    public Task<IFont> LoadFontAsync(FontSource src);
    public PathBuilder TextPath(IFont font, string text, float x, float y, float size, TextOptions opts);
    public void FillText(IFont font, string text, float x, float y, float size, Rgba8 color, TextOptions opts);
    public void StrokeText(IFont font, string text, float x, float y, float size, float width, Rgba8 color, TextOptions opts);

    // SVG helpers
    public PathBuilder SvgPath(string d);
    public void FillSvg(string d, Rgba8 color, FillRule rule = FillRule.EvenOdd);
    public void StrokeSvg(string d, float width, Rgba8 color, StrokeStyle style);

    // Clip / opacity / mask stacks
    public void PushClip(PathBuilder path, FillRule rule = FillRule.EvenOdd);
    public void PopClip();
    public void PushOpacity(float alpha);
    public void PopOpacity();
    public void PushOpacityMask(PathBuilder path, float alpha = 1f, FillRule rule = FillRule.EvenOdd);
    public void PopOpacityMask();

    public PackedScene Build();
}
```

## Supporting Types
- `PathBuilder`: retains subpath commands (`MoveTo`, `LineTo`, `QuadTo`, `CubicTo`, `Arc`, `Ellipse`, `ClosePath`, `Transform`). Stores transforms lazily and supports `Flatten(float tolerance)` generating `Span<float>` of XY pairs.
- `StrokeStyle`: join/cap/miter limit enums (`round`, `bevel`, `miter`, `butt`, `square`).
- `TextOptions`: alignment, letter-spacing, optional transform; text path generation uses HarfBuzzSharp + SharpFont (or Skia) but exposes interface.
- `IFont`: abstraction returned by `LoadFontAsync`, with info for path extraction (glyph outlines) and metrics caching.
- `SceneBuilder`: internal recorder that tracks `List<ShapeWorkItem>` with references to clip/mask stacks. Responsibilities:
  - Flatten paths using same tolerance (default 0.35) as JS, returning closed polygons for fills.
  - Stroke paths by expanding polyline segments via `StrokeRasterizer` replicating JS `strokeToPolys` (normals, joins, caps, miter limit checks).
  - Maintain stacks (`clipStack`, `maskStack`, `opacityStack`) pushing arrays of IDs (since a path may produce multiple polygons).
  - On `Build()`, produce `PackedScene` with:
    - `float[] Vertices` containing interleaved XY values for shapes, clips, masks
    - `ShapeGpu[] Shapes`, `ClipGpu[] Clips`, `MaskGpu[] Masks`
    - `uint[] ClipRefs`, `uint[] MaskRefs`, concatenated into `uint[] Refs`
    - `uint[] TileOC`, `uint[] TileShapeIndex` via CPU tiler (Task 3)
    - `UniformsGpu` filled from canvas/tile parameters.

## Sample Usage
```csharp
var api = new VectorApi(width: 1024, height: 768);
var yellow = api.Color(250, 245, 140, 255);
var blue   = api.Color(88, 156, 255, 220);
var star   = api.Path().Poly(api.Star(512, 300, 240, 100, 7)).ClosePath();
api.FillPath(star, blue);
api.StrokePath(api.Path().Poly(api.RandomPolyline(220)), 10, yellow,
               new StrokeStyle { Join = StrokeJoin.Round, Cap = StrokeCap.Round, MiterLimit = 4 });
var scene = api.Build(); // -> PackedScene consumed by GPU backend
```

## Acceptance Checklist (Task 2)
- [x] API surface defined, matching the JS commands and stacks described in the chat history.
- [x] Supporting structures enumerated with responsibilities (PathBuilder, StrokeRasterizer, Text pipeline, SceneBuilder, PackedScene output).
- [x] Sample usage code demonstrates star + stroke parity scenario.
- [x] Deliverable stored under `docs/vector-api-design.md` for future implementation reference.

Next action: proceed to Task 3 (geometry packing & CPU tiler) using this API contract.
