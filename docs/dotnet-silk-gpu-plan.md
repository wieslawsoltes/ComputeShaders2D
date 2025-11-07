# .NET Silk.NET WebGPU + SkiaSharp Avalonia Renderer Plan

Context pulled from the shared chat history: reuse the existing WGSL compute shader vector renderer inside a native .NET stack, surface the same high-level drawing API, run the compute pass through Silk.NET.WebGPU, and present the result inside Avalonia via SkiaSharp (copy bridge first, zero-copy later). Each numbered task below is checkable and carries detailed acceptance notes plus key snippets quoted from the chat for reference.

1. [x] Requirements & architecture baseline
   - Capture product goals (API parity with JS demo, cross-platform desktop focus, WGSL reuse, Avalonia UI embedding) and constraints (Silk.NET.WebGPU for device access, SkiaSharp for UI composition, optional ComputeSharp/D3D12 fallback on Windows).
   - Decide default supersampling (SSAA), tiling, and data packing (Shape=64B, Clip=16B, Mask=32B, row alignment 256 B) exactly as the WGSL expects.
   - Deliverable: concise ADR describing why WebGPU via Silk.NET is chosen over other bindings and when to switch to ComputeSharp per the chat guidance.

2. [x] Authoring API parity & scene builder
   - Implement the C# `VectorApi` that mirrors the JS playground API (paths, fills, strokes, random polylines, time accessors, clip/opacity stacks, SVG/text helpers).
   - Port the path/stroke flatteners (De Casteljau subdivision, arc tessellation, stroke joins/caps) and ensure clip/mask stacks record IDs exactly like the JS builder.
   - Use the skeleton shared in the chat as the starting contract:
     ```csharp
     public sealed class VectorApi
     {
         public int Width { get; }
         public int Height { get; }
         public PathBuilder Path() => new();
         public void FillPath(PathBuilder p, Rgba8 c, FillRule rule = FillRule.EvenOdd) => _sb.Fill(p, c, rule);
         public void PushClip(PathBuilder p, FillRule r = FillRule.EvenOdd) => _sb.PushClip(p, r);
         public void PushOpacityMask(PathBuilder p, float a = 1f, FillRule r = FillRule.EvenOdd) => _sb.PushMask(p, a, r);
         // …additional helpers (Color, RandomPolyline, TextPath, StrokeText, etc.)
     }
     ```
   - Acceptance: authoring sample scenes (star + stroke + text + clip/mask) serialize into a `PackedScene` identical to the JS buffers.

3. [x] Geometry packing & CPU tiler
   - Define `[StructLayout(LayoutKind.Sequential, Pack = 4)]` structs for `ShapeGpu`, `ClipGpu`, `MaskGpu`, and `UniformsGpu` exactly as in the conversation (16×4-byte slots per shape, 8×4-byte uniforms, combined refs buffer with clip refs followed by mask refs).
   - Port the CPU tiling pipeline (compute bounding boxes, count shapes per tile, exclusive scan, scatter into `tileShapeIndex`) to C# using `Span<uint>`/`ArrayPool` to minimize allocations.
   - Emit combined refs buffer and 256-byte-padded vertex arrays (shared by shapes, clips, masks) to match the WGSL storage layout.

4. [x] Silk.NET WebGPU backend & compute dispatch
   - Stand up the `WgpuRenderer` host identical to the chat outline: create instance/adapter/device, upload WGSL shaders, allocate the storage texture, and keep a 256-byte aligned readback buffer.
   - Bind all storage/uniform buffers via a single bind group (bindings 0–8) exactly as the WGSL `@group(0)` layout expects; enforce the storage-buffer-count≤8 rule noted in the chat.
   - Reuse the shared C# snippet for structure:
     ```csharp
     sealed class WgpuVectorRenderer : IDisposable
     {
         WebGPU api = WebGPU.GetApi();
         Instance* instance; Adapter* adapter; Device* device; Queue* queue;
         Texture* outputTex; TextureView* outputView; Buffer* readback;
         ComputePipeline* compute; RenderPipeline* blit; BindGroup* computeBG;

         public void RenderOnceAndReadback(Span<byte> dst)
         {
             // dispatch compute (8×8 workgroups)
             // copy storage texture → readback buffer
             // map row-aligned data into dst
         }
     }
     ```
   - Acceptance: run the WGSL compute pass headless and retrieve straight RGBA frames on macOS, Windows, and Linux using the same WGSL source that ships in the JS demo.

5. [x] Avalonia + SkiaSharp presentation (copy bridge)
   - Wrap the renderer inside an Avalonia `Control` (or `OpenGlControlBase` descendant) that schedules frames via `DispatcherTimer` and copies the mapped buffer into a `WriteableBitmap` or `SKImage` every tick, as suggested in the chat.
   - Implement the sample glue outlined earlier:
     ```csharp
     public sealed class GpuCanvas : Control, IDisposable
     {
         readonly WriteableBitmap _wb;
         readonly byte[] _scratch;
         readonly WgpuVectorRenderer _gpu;

         void RenderFrame()
         {
             _gpu.RenderOnceAndReadback(_scratch);
             using var fb = _wb.Lock();
             CopyRows(_scratch, fb.Address, fb.RowBytes);
             InvalidateVisual();
         }

         public override void Render(DrawingContext ctx)
             => ctx.DrawImage(_wb, new Rect(Bounds.Size));
     }
     ```
   - Ensure pixel formats stay consistent (WGSL outputs straight RGBA; choose `PixelFormats.Rgba8888` w/ `AlphaFormat.Unpremul` for Avalonia, or pre-multiply in shader and upload as Premul).

6. [x] SkiaSharp GPU pipeline integration modes
   - **Copy bridge (baseline):** Convert the readback buffer into an `SKImage` (`SKImage.FromPixels(info, ptr, rowBytes)`) and compose inside Skia/Avalonia; validate bandwidth at target resolutions (e.g., 1920×1080 @ 60 FPS ≈ 500 MB/s memory traffic).
   - **Zero-copy future:** Design the native shim that wraps a `WGPUTexture` into Skia Graphite’s Dawn backend (`skgpu::graphite::Context::MakeDawn` + `BackendTextures::MakeDawn`), as explicitly described in the chat. Document dependencies (Skia built with Graphite+Dawn, small C++ DLL with P/Invoke exports) and synchronization strategy (queue submission + Graphite recorder flush).
   - Acceptance: documented comparison of both modes plus a spike branch validating the copy bridge end-to-end.

7. [x] Sample app, diagnostics, and fallbacks
   - Build a showcase Avalonia app that mirrors the browser UI: renderer toggle (CPU/WebGPU), canvas size, tile size, stroke width, join/cap selectors, and buttons to load the clip/mask animation samples.
   - Provide optional Windows-only ComputeSharp backend per the chat recommendation for environments where WebGPU isn’t available yet; keep the same `PackedScene` buffers so backends can be swapped via an interface.
   - Add diagnostic overlays (build/raster times, FPS, tile counts) exactly like the JS UI for parity.

8. [x] Validation, packaging, and docs
   - Write integration tests for geometry packing (spot-check shape→vertex ranges, clip/mask reference ranges, tile binning counts) and smoke tests for GPU dispatch (headless render, hash outputs).
   - Document build steps, runtime dependencies (Vulkan/Metal drivers, Dawn native libraries, Avalonia assets), and provide troubleshooting tips (e.g., WebGPU adapter selection, row-pitch alignment errors).
   - Publish final README section summarizing how to extend toward the zero-copy Skia Graphite path, referencing the chat’s source links (DawnTexture::MakeWrapped, BackendTextures::MakeDawn, etc.).

Once every checkbox above is satisfied, you’ll have a full-fidelity .NET reimplementation of the JS compute shader renderer, integrated with Avalonia/SkiaSharp today and ready for future zero-copy WebGPU↔Skia interop.

## Actionable Implementation Checklist

The review surfaced several regressions relative to the chat-proposed architecture. Track them here and check off as code aligns with the design.

1. [x] **Authoring parity:** Extend `VectorApi` with the missing SVG/text helpers (`LoadFontAsync`, `TextPath`, `FillText`, `StrokeText`, `SvgPath`, `FillSvg`, `StrokeSvg`) so scripted scenes from the JS demo can compile unchanged. Ensure these APIs delegate into `SceneBuilder`.
2. [x] **Stroke fidelity:** Replace the placeholder stroker with the full JS-equivalent join/cap/miter logic so round/bevel/miter joins and butt/round/square caps render correctly and honor `StrokeStyle`.
3. [x] **Animation plumbing:** Store `TimeSeconds`, `DeltaSeconds`, and `Frame` inside `SceneBuilder`/`PackedScene` (or the render uniforms) rather than local `VectorApi` properties so GPU backends can read them.
4. [x] **Shader packaging:** Move the WGSL compute/blit sources into embedded resources (as planned in `docs/validation-packaging.md`) and load them at runtime instead of keeping giant inline strings.
5. [x] **ComputeSharp fallback:** Windows builds now compile the full ComputeSharp pipeline (guarded behind MSBuild OS conditions) while non-Windows builds keep the CPU proxy. Avalonia can switch between CPU/WebGPU/ComputeSharp, and the renderer exposes a deterministic frame hash for validation.
6. [ ] **Allocator hygiene:** Update `TileBinner` (and any other hot path) to reuse buffers via `Span`/`ArrayPool` to avoid per-frame allocations at large resolutions.
7. [ ] **Testing coverage:** Augment `tests/ComputeShaders2D.Tests` with the geometry-packing and renderer smoke tests enumerated in `docs/validation-packaging.md` once the above items land.

Each item should land with targeted unit tests (where feasible) plus documentation updates noting the new capabilities.
