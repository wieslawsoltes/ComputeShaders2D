# Geometry Packing & CPU Tiler (Task 3)

This document completes Task 3 from `docs/dotnet-silk-gpu-plan.md` by detailing the managed data layout that mirrors the WGSL structs plus the CPU tiling/binning pipeline.

## GPU-Facing Structs
_All structs use `[StructLayout(LayoutKind.Sequential, Pack = 4)]` to keep 4-byte alignment and match WGSL expectations._

```csharp
public enum FillRule : uint { EvenOdd = 0, NonZero = 1; }

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ShapeGpu
{
    public uint VStart;      // index into shared vertex buffer (vec2f array)
    public uint VCount;      // number of vertices (pairs)
    public FillRule Rule;    // 0 = even-odd, 1 = non-zero
    public uint _pad0;

    public float ColorR;     // premultiplied RGBA
    public float ColorG;
    public float ColorB;
    public float ColorA;

    public uint ClipStart;   // offset into refs buffer for clip IDs
    public uint ClipCount;
    public uint MaskStart;   // offset into refs buffer for mask IDs (after clip section)
    public uint MaskCount;

    public float Opacity;    // multiplicative opacity stack result
    public float _pad1;
    public float _pad2;
    public float _pad3;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ClipGpu
{
    public uint VStart;
    public uint VCount;
    public FillRule Rule;
    public uint _pad0;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MaskGpu
{
    public uint VStart;
    public uint VCount;
    public FillRule Rule;
    public uint _pad0;
    public float Alpha;      // [0,1]
    public float _pad1;
    public float _pad2;
    public float _pad3;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct UniformsGpu
{
    public uint CanvasW;
    public uint CanvasH;
    public uint TileSize;
    public uint TilesX;
    public uint Supersample; // SS factor (1,2,4)
    public uint _padA;
    public uint _padB;
    public uint _padC;
}
```

## PackedScene Output
`SceneBuilder.Build()` emits:
- `float[] Vertices`: shared XY pairs for shapes, clips, masks (order preserved for mapping indices).
- `ShapeGpu[] Shapes`, `ClipGpu[] Clips`, `MaskGpu[] Masks`.
- `uint[] ClipRefs`, `uint[] MaskRefs` (concatenated into combined `uint[] Refs` during upload).
- `uint[] TileOffsetCount` (tileOC) and `uint[] TileShapeIndex` (tileShapeIx) per the WGSL buffers.
- `UniformsGpu Uniforms` populated from canvas size, tile size, and SSAA settings.

## CPU Tiling/Binning Pipeline
The tiler mirrors the JS reference:

1. **Bounds:** For each shape polygon, compute min/max XY to find tile coverage range.
2. **Tile grid:**
   ```csharp
   int tilesX = (int)Math.Ceiling(width / (float)tileSize);
   int tilesY = (int)Math.Ceiling(height / (float)tileSize);
   int tileCount = tilesX * tilesY;
   ```
3. **Counts pass:** Iterate shapes, convert bounds to tile indices `(minTx..maxTx, minTy..maxTy)`, increment `counts[t]` for each covered tile. Store the range per shape for reuse.
4. **Exclusive scan:** Run prefix sum over `counts` to produce `offsets[t]` and total list length.
   ```csharp
   for (int i = 0; i < tileCount; i++)
   {
       offsets[i] = running;
       running += counts[i];
   }
   ```
5. **Scatter:** Allocate `tileShapeIndex` with `running` length; reuse the stored `(minTx..maxTx, minTy..maxTy)` per shape to scatter indices using a scratch cursor array (`cursors[t] = offsets[t]`).
6. **Pack tile metadata:** Combine `offsets` and `counts` into a single `uint[] tileOC` laid out `[off0,count0, off1,count1, ...]` to honor the “<= 8 storage buffers” constraint called out in the conversation.

### Implementation Notes
- Use `Span<int>` / `ArrayPool<int>` to avoid large heap churn for `counts`, `offsets`, `cursors`.
- All vertex positions stay in device pixels; tiler works in the same space with no normalization.
- Tile coverage is inclusive: clamp to `[0, tilesX-1]` / `[0, tilesY-1]`.
- Shapes with zero area (less than 3 points) are skipped before tiling.
- Keep deterministic ordering: shapes are appended in submission order; `tileShapeIndex` preserves that, so blending matches JS.

## Acceptance Checklist (Task 3)
- [x] Struct definitions captured with exact sizes and packing requirements.
- [x] PackedScene contract documented with buffer sets.
- [x] Tiler algorithm specified step-by-step, including combined offset/count packing and resource constraints.
- [x] File stored as `docs/geometry-packing.md` for reference while implementing CPU packing code.

Next: proceed to Task 4 (Silk.NET WebGPU backend & compute dispatch) using these structures/buffers.
