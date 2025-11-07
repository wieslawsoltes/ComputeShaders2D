# SkiaSharp Integration Modes (Task 6)

Task 6 from `docs/dotnet-silk-gpu-plan.md` requires documenting both the baseline copy bridge and the future zero-copy path between the Silk.NET WebGPU renderer and SkiaSharp.

## Mode A — Copy Bridge (Current Shipping Path)

**Pipeline**
1. `WgpuVectorRenderer.Render()` writes the frame into an RGBA8 storage texture and copies it into the row-aligned readback buffer.
2. Avalonia/Skia copies rows into either a `WriteableBitmap` (for `DrawingContext`) or an `SKImage` (for direct Skia drawing), as described in Task 5.

**Pros**
- Works today on macOS/Windows/Linux with no native glue beyond Silk.NET.
- Keeps SkiaSharp usage purely managed.
- Simple synchronization: wait for `QueueSubmit` completion, copy rows, render.

**Cons**
- Requires CPU bandwidth (~8.3 MB per 1080p frame). At 60 FPS this is ~500 MB/s memory traffic.
- Extra latency (GPU→CPU→GPU) before the pixels land in Skia.

**Implementation Notes**
- Keep buffers pinned only while copying; reuse scratch arrays to avoid GC pressure.
- When using Skia directly:
  ```csharp
  var info = new SKImageInfo((int)width, (int)height,
                             SKColorType.Rgba8888, SKAlphaType.Unpremul);
  using var image = SKImage.FromPixels(info, gpuPtr, (int)rowPitch);
  canvas.DrawImage(image, 0, 0);
  ```
- Ensure WGSL outputs straight RGBA (as in the JS reference) to align with `AlphaFormat.Unpremul`; if switching to premultiplied output later, update both shader and bitmap usage.

## Mode B — Zero-Copy via Skia Graphite + Dawn (Future)

**Goal**
Allow SkiaSharp to draw directly from (or write directly into) the WebGPU storage texture without GPU→CPU copying.

**Ingredients**
- Skia Graphite built with the Dawn (WebGPU) backend enabled.
- Access to the native WebGPU device/queue/texture handles exposed by Silk.NET (`WGPUDevice*`, `WGPUTexture*`).
- A thin native shim (C/C++) callable from .NET (P/Invoke) that:
  1. Wraps the existing `WGPUDevice` inside `skgpu::graphite::DawnBackendContext` (`Context::MakeDawn`).
  2. Wraps a `WGPUTextureView` via `skgpu::graphite::BackendTextures::MakeDawn(...)`.
  3. Creates an `SkSurface`/`SkImage` from that backend texture.
  4. Exposes handles back to managed code for drawing.

**Rough C++ Sketch**
```cpp
extern "C" SkSurface* SkiaGraphite_WrapWgpuTexture(WGPUDevice device,
                                                   WGPUQueue queue,
                                                   WGPUTextureView view,
                                                   int width,
                                                   int height)
{
    skgpu::graphite::DawnBackendContext be;
    be.fDevice = wgpu::Device::Acquire(device);
    be.fQueue  = wgpu::Queue::Acquire(queue);

    auto context = skgpu::graphite::Context::MakeDawn(be);
    auto recorder = context->makeRecorder();

    skgpu::graphite::DawnTextureInfo info;
    info.fFormat      = wgpu::TextureFormat::RGBA8Unorm;
    info.fUsage       = wgpu::TextureUsage::TextureBinding | wgpu::TextureUsage::RenderAttachment;
    info.fSampleCount = 1;

    auto backendTex = skgpu::graphite::BackendTextures::MakeDawn(
        {width, height}, info, view);

    auto surface = SkSurfaces::WrapBackendTexture(
        recorder.get(), backendTex,
        kTopLeft_GrSurfaceOrigin, /*sample count*/1,
        kRGBA_8888_SkColorType,
        nullptr, nullptr);

    // ... manage lifetime, flushes, etc.
    return surface.release();
}
```

**Managed Usage**
- Expose a `SkiaGraphiteInterop` service that takes ownership of the WebGPU device + storage texture.
- Provide methods `AcquireSkSurface()` / `Submit()` mirroring Graphite workflows.

**Synchronization**
- Ensure WebGPU compute pass is submitted before Skia begins reading/writing the texture.
- Flush/submit Graphite recorder work before handing the texture back to WebGPU for the next frame.
- Coordinate resource ownership if the texture is also written by Skia (e.g., Skia draws UI overlays on top of compute result).

**Status**
- Requires custom native build and SkiaSharp exposing Graphite handles. Track as future work item once Graphite bindings are available or the shim is implemented.

## Acceptance Checklist (Task 6)
- [x] Baseline copy bridge spelled out with pros/cons and Skia/Avalonia hooks.
- [x] Zero-copy mode described with required native components, references, and synchronization considerations.
- [x] Document stored as `docs/skia-integration-modes.md` for future implementation.

Next up: Task 7 (sample app, diagnostics, and fallback backends).
