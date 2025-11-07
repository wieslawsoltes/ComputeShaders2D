# Silk.NET WebGPU Backend Design (Task 4)

This document satisfies Task 4 from `docs/dotnet-silk-gpu-plan.md` by describing the `WgpuVectorRenderer` host that uploads the packed scene buffers, dispatches the WGSL compute shader, and exposes frame readback for Skia/Avalonia integration.

## Goals
- Reuse the existing WGSL compute + blit shaders verbatim.
- Support Vulkan/Metal/D3D12 through wgpu via Silk.NET.
- Maintain buffer/texture lifetimes for eventual zero-copy interop.

## Key Components

### Initialization
```csharp
sealed unsafe class WgpuVectorRenderer : IDisposable
{
    private readonly WebGPU _api = WebGPU.GetApi();
    private Instance* _instance;
    private Adapter*  _adapter;
    private Device*   _device;
    private Queue*    _queue;

    private Texture* _outputTex;
    private TextureView* _outputView;
    private Buffer* _readback;

    private ComputePipeline* _compute;
    private BindGroup* _computeBG;

    // Optional: render pipeline + sampler if we keep the GPU blit path
    private RenderPipeline* _blit;
    private BindGroup* _blitBG;

    public WgpuVectorRenderer(uint width, uint height, uint tileSize, uint supersample)
    {
        _instance = _api.CreateInstance(new InstanceDescriptor());
        _adapter  = RequestAdapter(_api, _instance);
        _device   = RequestDevice(_api, _adapter);
        _queue    = _api.DeviceGetQueue(_device);

        _compute = CreateComputePipeline(_device, Shaders.WgslCompute, "main");
        _blit    = CreateBlitPipeline(_device, Shaders.WgslBlit, TextureFormat.Rgba8Unorm);

        _outputTex = _api.DeviceCreateTexture(_device, new TextureDescriptor
        {
            Size   = new Extent3D(width, height, 1),
            Format = TextureFormat.Rgba8Unorm,
            Usage  = TextureUsage.StorageBinding | TextureUsage.TextureBinding | TextureUsage.CopySrc
        });
        _outputView = _api.TextureCreateView(_outputTex, null);

        uint rowBytes = ((width * 4u) + 255u) & ~255u; // 256-byte alignment
        _readback = _api.DeviceCreateBuffer(_device, new BufferDescriptor
        {
            Size  = (nuint)(rowBytes * height),
            Usage = BufferUsage.MapRead | BufferUsage.CopyDst
        });

        Width = width;
        Height = height;
        TileSize = tileSize;
        Supersample = supersample;
        RowPitch = rowBytes;
    }

    public uint Width { get; }
    public uint Height { get; }
    public uint TileSize { get; }
    public uint Supersample { get; }
    public uint RowPitch { get; }

    public void Dispose() { /* destroy resources */ }
}
```

### Buffer Upload Helpers
`CreateBuffer(Device*, ReadOnlySpan<T>, BufferUsageFlags)` maps staging memory, copies the span, and unmaps.

During `RenderFrame(PackedScene scene)`:
```csharp
var (bUniforms, bShapes, bVerts, bTileOC, bTileIx, bClips, bMasks, bRefs) = UploadBuffers(scene);
_computeBG = _api.DeviceCreateBindGroup(_device, new BindGroupDescriptor
{
    Layout = _api.ComputePipelineGetBindGroupLayout(_compute, 0),
    Entries = new[]
    {
        new BindGroupEntry { Binding = 0, Buffer = bUniforms },
        new BindGroupEntry { Binding = 1, Buffer = bShapes },
        new BindGroupEntry { Binding = 2, Buffer = bVerts },
        new BindGroupEntry { Binding = 3, Buffer = bTileOC },
        new BindGroupEntry { Binding = 4, Buffer = bTileIx },
        new BindGroupEntry { Binding = 5, Buffer = bClips },
        new BindGroupEntry { Binding = 6, Buffer = bMasks },
        new BindGroupEntry { Binding = 7, Buffer = bRefs },
        new BindGroupEntry { Binding = 8, TextureView = _outputView },
    }
});
```
This matches the WGSL `@group(0)` layout and keeps the total storage buffers ≤ 8 (clip/mask refs combined).

### Dispatch
```csharp
public void Render(PackedScene scene)
{
    using var buffers = Upload(scene); // returns disposable holder for GPU buffers

    var encoder = _api.DeviceCreateCommandEncoder(_device, null);

    var cpass = _api.CommandEncoderBeginComputePass(encoder, null);
    _api.ComputePassEncoderSetPipeline(cpass, _compute);
    _api.ComputePassEncoderSetBindGroup(cpass, 0, _computeBG, 0, null);
    const uint WG = 8;
    _api.ComputePassEncoderDispatchWorkgroups(cpass,
        (Width  + WG - 1) / WG,
        (Height + WG - 1) / WG,
        1);
    _api.ComputePassEncoderEnd(cpass);

    _api.CommandEncoderCopyTextureToBuffer(encoder,
        new ImageCopyTexture { Texture = _outputTex },
        new ImageCopyBuffer
        {
            Buffer = _readback,
            Layout = new TextureDataLayout
            {
                BytesPerRow  = RowPitch,
                RowsPerImage = Height
            }
        },
        new Extent3D(Width, Height, 1));

    var commandBuffer = _api.CommandEncoderFinish(encoder, null);
    _api.QueueSubmit(_queue, 1, &commandBuffer);
}
```

### Readback
```csharp
public ReadOnlySpan<byte> MapReadback()
{
    _api.BufferMapAsync(_readback, MapMode.Read, 0, RowPitch * Height, null, null);
    _api.DevicePoll(_device, true);
    var ptr = _api.BufferGetMappedRange(_readback, 0, RowPitch * Height);
    return new Span<byte>(ptr, checked((int)(RowPitch * Height)));
}

public void UnmapReadback() => _api.BufferUnmap(_readback);
```
The consumer copies each row into a `WriteableBitmap` or `SKImage`, accounting for `RowPitch` vs. canvas width.

### Optional GPU Blit
If we keep the WGSL blit shader for diagnostics, we create a render pipeline that samples `_outputView` and draws a full-screen triangle into a swapchain or offscreen texture for validation.

### Error Handling & Validation
- Check `RequestAdapter`/`RequestDevice` return codes; fall back to CPU renderer or ComputeSharp backend when unavailable.
- Validate `scene.Vertices.Length` doesn’t exceed `uint.MaxValue`/2 (since indices are uint).
- Ensure row pitch is recomputed if the canvas size changes; recreate `_outputTex`, `_outputView`, and `_readback` accordingly.
- Keep `CommandEncoder`, `BindGroup`, and staging buffers short-lived per frame; future optimization: persistent buffers + queue writes.

## Acceptance Checklist (Task 4)
- [x] Initialization outlined (instance/adapter/device, shader compilation, texture/readback allocation).
- [x] Bind group layout and buffer uploads documented to match WGSL.
- [x] Dispatch and readback sequences specified, including row-pitch alignment and compute workgroup sizing.
- [x] Design captured in `docs/wgpu-backend-design.md` for implementation reference.

Next: move to Task 5 (Avalonia + SkiaSharp presentation) armed with the renderer contract above.
