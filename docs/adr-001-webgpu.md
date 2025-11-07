# ADR-001: Renderer Architecture & Technology Selection

## Status
Accepted — 2024-06-xx

## Context
The existing prototype is a browser-based WebGPU/vector renderer that builds complex path scenes using a custom API and executes a compute shader (WGSL) to rasterize tiles with SSAA, clip stacks, and opacity masks. The goal is to:
- Ship a cross-platform desktop renderer (macOS, Windows, Linux) with Avalonia UI for tooling and UX parity with the browser demo.
- Reuse the proven WGSL compute + blit shaders and CPU geometry pipeline without rewriting them for every backend.
- Surface the exact same authoring API (paths, stroking, clips, text, animation parameters) in C# so scenes and tests stay portable between JS and .NET.
- Integrate cleanly with SkiaSharp/Avalonia’s composition pipeline today, while keeping a path open to a zero-copy GPU handoff later.

Key constraints gathered from the shared chat history:
1. **WebGPU bindings:** Silk.NET.WebGPU offers an actively maintained native binding to wgpu (Vulkan/Metal/D3D12 backends) and lets us pass WGSL verbatim. Alternatives like Evergine/WebGPU.NET have pivoted to browser-only support, and ComputeSharp (DX12) would be Windows-only.
2. **Swapchain limitations:** WebGPU compute shaders cannot write directly to a surface/swapchain image, so we must render into a storage texture and then either blit or copy.
3. **SkiaSharp interop:** Avalonia apps render through Skia. Today’s lowest-friction approach is to copy the GPU result into a `WriteableBitmap`/`SKImage`. Long term, Skia Graphite’s Dawn backend can adopt a `WGPUTexture` for zero-copy integration, but that requires a native shim until SkiaSharp exposes Graphite.
4. **Optional backends:** To cover environments where WebGPU is unavailable or for pure-Windows deployments, ComputeSharp (C# → HLSL on DX12) remains a viable optional backend, but not the primary target.

## Decision
1. **Use Silk.NET.WebGPU for the primary GPU backend.**
   - Keeps the WGSL compute shader unchanged (8×8 workgroups, SSAA, clip/mask logic, premultiplied blending) and uses the same buffer layouts (Shape=64B, Clip=16B, Mask=32B).
   - Runs on Vulkan/Metal/D3D12 via wgpu, satisfying cross-platform requirements.

2. **Maintain a two-stage presentation pipeline:**
   - Stage A: compute pass writes into an `rgba8unorm` storage texture plus optional full-screen blit for debugging.
   - Stage B: copy the storage texture to a CPU readback buffer (row-pitch aligned) and feed it into SkiaSharp (`WriteableBitmap`/`SKImage`) for Avalonia composition.

3. **Authoring layer:**
   - Implement a managed `VectorApi` + `SceneBuilder` that mirrors the JS API so sample scenes, clips, and animation scripts port 1:1.
   - Geometry flattening, stroking, and tile binning stay on the CPU exactly as in the JS reference.

4. **Future-proofing for zero-copy:**
   - Document the extension path to wrap WebGPU textures with Skia Graphite’s Dawn backend (using `skgpu::graphite::Context::MakeDawn` and `BackendTextures::MakeDawn`) once SkiaSharp exposes Graphite or a small native bridge is added.
   - Keep buffer/texture lifetimes explicit so we can share them between WebGPU and Skia without extra copies later.

## Alternatives Considered
- **ComputeSharp-only pipeline:** Attractive for Windows but would require rewriting the compute shader in C# (HLSL) and drops macOS/Linux.
- **Evergine/WebGPU.NET:** No longer suitable for native desktop targets per the shared conversation notes.
- **Render entirely through Skia GPU backends (Vulkan/Metal) without WebGPU:** Would forfeit WGSL reuse and require reauthoring the compute shader in GLSL/HLSL/SPIR-V, duplicating effort.

## Consequences & Tasks
- Implement the Silk.NET.WebGPU wrapper (`WgpuVectorRenderer`) that uploads the shared buffers, dispatches the WGSL compute, and exposes a readback API for Skia.
- Build the Avalonia control that copies GPU frames into a `WriteableBitmap`/`SKImage` each tick.
- Track a work item for the future Graphite/Dawn zero-copy bridge.
- Optional: expose a `ComputeSharpVectorRenderer` behind a feature flag for Windows DX12-only environments.

This ADR fulfills Task 1 from `docs/dotnet-silk-gpu-plan.md` (Requirements & architecture baseline) by capturing the goals, constraints, decision, and trade-offs.
