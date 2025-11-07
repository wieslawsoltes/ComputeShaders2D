# Sample App, Diagnostics & Fallbacks (Task 7)

Covers Task 7 from `docs/dotnet-silk-gpu-plan.md`: define the Avalonia sample app, matching controls, diagnostics, and backend fallbacks.

## App Structure
- Avalonia desktop app targeting macOS/Windows/Linux.
- Main window hosts two primary regions:
  1. **Control panel** (mirrors browser UI): tiles, renderer mode, stroke options, sample loaders.
  2. **Render preview** (the `GpuCanvas` control).
- MVVM-friendly design: `RendererViewModel` exposes observable properties (tile size, AA, etc.) bound to both UI and the `SceneProvider` that builds `PackedScene` objects each frame.

## UI Parity Requirements
- Dropdowns/toggles for:
  - Renderer: `Auto | WebGPU | CPU | ComputeSharp (Windows)`
  - Canvas Size: (800×600, 1024×768, 1280×720, 1920×1080)
  - Tile size slider (16–128 step 16)
  - Workers (for CPU path)
  - Stroke width slider
  - Join / Cap selectors
  - Miter limit slider
  - AA (1×, 2×, 4×)
- Buttons:
  - `Render`, `Randomize Lines`, `Toggle Fill Rule`
  - `Play/Pause`, `Reset Time`, `Load Animation Sample`, `Load Clip Sample`, `Load Mask Sample`
- Text area (optional) or asset dropdown to choose the animation script.

## Diagnostics Overlay
- Display metrics identical to the JS demo:
  - Build time (ms)
  - Raster time (ms)
  - Tiles count (e.g., “20 × 12 = 240”)
  - FPS (EMA) and per-frame time
  - Mode indicator (“Static” / “Animating”)
- Implementation: Use `TextBlock`s bound to `RendererStats` updated after each frame (make thread-safe via `Dispatcher.UIThread.Post`).

## Backend Abstraction
```csharp
public interface IVectorRenderer : IDisposable
{
    bool IsAvailable { get; }
    RendererType Type { get; }
    void Render(PackedScene scene, Span<byte> scratchBuffer);
    uint RowPitch { get; }
}
```
- **WebGPU** backend (`WgpuVectorRenderer`): primary path.
- **CPU fallback**: re-use existing JS algorithms ported to C# for determinism; renders directly into the scratch buffer (no GPU involvement).
- **ComputeSharp backend** (optional Windows feature flag): dispatches C# compute shader for environments without WebGPU.
- `RendererSelector` picks the highest-priority available backend based on the UI selection and runtime capabilities.

### Fallback Behavior & TODOs
- Surface which backend is active directly in the UI (e.g., status pill) and log when the renderer swaps back to the CPU path because device creation/dispatch failed.
- Track TODO work items for the CPU fallback (tile raster, clip/mask support) so automated tests can validate both GPU and CPU implementations.
- Expose a developer toggle to force CPU mode, allowing deterministic reproduction of fallback-specific bugs.

## Scene Samples
- Port the three JS samples as C# scripts or embedded resources:
  1. Static default (star + strokes + text)
  2. Animation sample (time-driven star + ribbon + text)
  3. Clip sample (`withClip`) and mask sample (`withOpacityMask`)
- Provide a small DSL or use Roslyn scripting so advanced users can author scenes without recompilation.

## Animation Loop
- `RendererLoop` service manages play/pause state, timeline values (time, dt, frame), and exposes events or callbacks to rebuild scenes.
- Hooked up to the `GpuCanvas`, which requests a new `PackedScene` from `SceneProvider` each frame.

## Testing Hooks
- Add command palette entries or CLI flags to run in headless mode rendering a single frame and saving PNG output (useful for regression tests).
- Provide instrumentation toggles (e.g., show per-tile outlines) for debugging.

## Acceptance Checklist (Task 7)
- [x] Control panel parity documented.
- [x] Diagnostics overlay requirements defined.
- [x] Backend interface + selection strategy specified, including CPU/ComputeSharp fallback.
- [x] Sample content strategy captured.
- [x] Document stored at `docs/sample-app-plan.md` for implementation guidance.

Next: move to Task 8 (validation, packaging, documentation).
