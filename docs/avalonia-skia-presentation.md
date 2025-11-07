# Avalonia + SkiaSharp Presentation (Task 5)

This document fulfills Task 5 from `docs/dotnet-silk-gpu-plan.md` by describing how the WebGPU renderer is embedded into an Avalonia control using SkiaSharp with a CPU copy bridge.

## Goals
- Display frames produced by `WgpuVectorRenderer` inside Avalonia UI.
- Keep implementation cross-platform (macOS/Windows/Linux).
- Provide a clean seam for future zero-copy presentation.

## Strategy
1. `WgpuVectorRenderer` renders into an RGBA8 storage texture and copies into a row-pitch-aligned readback buffer.
2. The Avalonia layer copies the mapped bytes into either:
   - `WriteableBitmap` (pure Avalonia API), or
   - `SKImage` (if directly using SkiaSharp drawing contexts).
3. A `DispatcherTimer` or composition loop triggers redraws at the desired frame rate.

## Control Implementation
```csharp
public sealed class GpuCanvas : Control, IDisposable
{
    private readonly WriteableBitmap _bitmap;
    private readonly byte[] _scratch;
    private readonly WgpuVectorRenderer _renderer;
    private readonly DispatcherTimer _timer;

    public GpuCanvas(int width, int height, uint tileSize, uint ssaa)
    {
        _bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormats.Rgba8888,
            AlphaFormat.Unpremul);

        _scratch = new byte[width * height * 4];
        _renderer = new WgpuVectorRenderer((uint)width, (uint)height, tileSize, ssaa);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => RenderFrame();
        _timer.Start();
    }

    private void RenderFrame()
    {
        var scene = SceneProvider.BuildFrame(); // uses VectorApi to construct PackedScene
        _renderer.Render(scene);

        // Map GPU buffer into scratch span
        var gpuSpan = _renderer.MapReadback();
        CopyRowsToScratch(gpuSpan, _scratch, _renderer.RowPitch, _renderer.Width);
        _renderer.UnmapReadback();

        using var fb = _bitmap.Lock();
        CopyScratchToFramebuffer(_scratch, fb.Address, fb.RowBytes, _renderer.Width, _renderer.Height);

        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
        => ctx.DrawImage(_bitmap, new Rect(Bounds.Size));

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= (_, _) => RenderFrame();
        _renderer.Dispose();
        _bitmap.Dispose();
    }
}
```

### Row Copy Helpers
```csharp
static void CopyRowsToScratch(ReadOnlySpan<byte> src, Span<byte> dst, uint srcPitch, uint width)
{
    uint rowBytes = width * 4;
    for (uint y = 0; y < dst.Length / rowBytes; y++)
    {
        src.Slice((int)(y * srcPitch), (int)rowBytes).CopyTo(dst.Slice((int)(y * rowBytes), (int)rowBytes));
    }
}
```
(Reverse for framebuffer with potentially different stride `fb.RowBytes`.)

## SkiaSharp Variant
If rendering inside a control that exposes `SKCanvas` (e.g., `SkiaView`):
```csharp
var info = new SKImageInfo((int)_renderer.Width, (int)_renderer.Height,
                           SKColorType.Rgba8888, SKAlphaType.Unpremul);
using var img = SKImage.FromPixels(info, gpuPtr, (int)_renderer.RowPitch);
canvas.DrawImage(img, 0, 0);
```
Ensure the GPU buffer stays mapped only for the duration of the draw.

## Considerations
- **Threading:** Run rendering on UI thread via `DispatcherTimer` for simplicity. Later optimize with background task + `Dispatcher.UIThread.Post`.
- **Resize:** Recreate `WriteableBitmap`, `_scratch`, and `WgpuVectorRenderer` when control size changes.
- **Frame pacing:** Expose FPS configuration; e.g., 60 Hz vs. on-demand.
- **Fallbacks:** If WebGPU init fails, fallback to CPU path or ComputeSharp backend with the same `PackedScene` input.
- **Pixel format:** WGSL currently writes straight RGBA; `AlphaFormat.Unpremul` keeps semantics aligned. If premultiplying in shader, switch to `AlphaFormat.Premul`.

## Acceptance Checklist (Task 5)
- [x] Control structure defined (bitmap allocation, timer loop, render integration).
- [x] Row-copy mechanics documented (row pitch vs. stride).
- [x] SkiaSharp drawing variant described.
- [x] Design captured in `docs/avalonia-skia-presentation.md` for implementation reference.

Next focus: Task 6 (SkiaSharp GPU pipeline integration modes, including zero-copy planning).
