# Validation, Packaging & Documentation (Task 8)

This completes Task 8 from `docs/dotnet-silk-gpu-plan.md` by outlining quality gates, packaging steps, and user-facing docs.

## Testing Strategy
### 1. Geometry Packing Tests
- Unit tests for `SceneBuilder` verifying:
  - Vertex ranges per shape/clip/mask match expected counts given known paths.
  - Clip/mask reference ranges line up and no overlaps occur.
  - Tile binning produces deterministic `counts`, `offsets`, and `tileShapeIndex` for synthetic scenes (e.g., 2 shapes covering distinct tiles).
- Use golden JSON fixtures (serialized `PackedScene`) to catch regressions.

### 2. GPU Smoke Tests
- Headless render tests per backend:
  - Render canonical scenes (star sample, clip sample) at 256×256 and hash the raw RGBA bytes to detect shader regressions.
  - For WebGPU, run in CI on Windows + Linux (Mesa) GPU-enabled agents; fall back to wgpu’s software backend where GPU not available.
- CPU vs WebGPU comparison tests: difference image (tolerance) to ensure parity.

### 3. Integration Tests
- Avalonia UI test harness that instantiates `GpuCanvas`, drives one frame, and confirms framebuffer contents (via screenshot or WriteableBitmap inspection).
- Performance assertions (optional) to ensure tile build+raster stays within expected bounds on sample scenes.

## Packaging & Distribution
- Multi-target .NET project layout:
  - `ComputeShaders2D.Core` (authoring API, scene builder, packing).
  - `ComputeShaders2D.WebGPU` (Silk.NET backend).
  - `ComputeShaders2D.AvaloniaApp` (sample UI, Skia integration).
- Publish Avalonia sample as self-contained binaries per OS using `dotnet publish -c Release -r win-x64/osx-arm64/linux-x64` with trimming disabled (due to reflection in Silk.NET/Avalonia).
- Bundle WGSL shader sources as embedded resources.
- Document prerequisites: GPU drivers supporting Vulkan/Metal/D3D12, `libwgpu_native` shipped via Silk.NET, etc.

## Documentation Deliverables
- Update `README.md` with:
  - Overview of renderer architecture (linking to `docs/dotnet-silk-gpu-plan.md`).
  - Build instructions (prereqs, commands, troubleshooting).
  - Feature list (API parity, SSAA, clips/masks, animation, CPU/WebGPU paths).
  - Screenshot/GIF from the Avalonia sample.
- Provide `docs/USAGE.md` (or extend README) with:
  - Instructions for authoring new scenes via `VectorApi`.
  - How to switch renderer backends at runtime.
  - Guidance on extending to zero-copy Skia (reference `docs/skia-integration-modes.md`).
- Add `CONTRIBUTING.md` detailing coding standards, test commands, and how to run validation suites.

## Release Checklist
- [ ] All tests (unit + integration) pass.
- [ ] Sample app renders correctly on Windows, macOS, Linux.
- [ ] README/USAGE updated with fresh screenshots.
- [ ] Version tagged (e.g., `v0.1.0`), release notes summarize major features and limitations.

## Acceptance Checklist (Task 8)
- [x] Testing plan covering geometry, GPU smoke, integration.
- [x] Packaging/distribution outline per platform.
- [x] Documentation deliverables enumerated (README, usage, contributing).
- [x] Stored as `docs/validation-packaging.md` to guide implementation.
