using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ComputeShaders2D.Core.Rendering;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WebGpuApi = Silk.NET.WebGPU.WebGPU;
using BufferHandle = Silk.NET.WebGPU.Buffer;

namespace ComputeShaders2D.WebGPU;

public sealed unsafe class WgpuVectorRenderer : IVectorRenderer
{
    private const uint WorkgroupSize = 8;
    private static readonly string ComputeShaderSource = ShaderLoader.Load("ComputeShaders2D.WebGPU.Shaders.compute.wgsl");

    private readonly WebGpuApi _api = WebGpuApi.GetApi();
    private CpuFallbackRenderer _fallback;
    private Instance* _instance;
    private Adapter* _adapter;
    private Device* _device;
    private Queue* _queue;
    private ShaderModule* _computeModule;
    private ComputePipeline* _computePipeline;
    private BindGroupLayout* _bindGroupLayout;
    private Texture* _outputTexture;
    private TextureView* _outputView;
    private BufferHandle* _readbackBuffer;
    private uint _width;
    private uint _height;
    private uint _rowPitch;
    private nuint _readbackSize;
    private bool _isAvailable;

    public WgpuVectorRenderer(uint width, uint height)
    {
        _width = width;
        _height = height;
        _fallback = new CpuFallbackRenderer(width, height);
        Initialize();
        if (_isAvailable)
        {
            EnsureCanvasResources(width, height);
        }
    }

    public bool IsAvailable => _isAvailable;
    public uint Width => _isAvailable ? _width : _fallback.Width;
    public uint Height => _isAvailable ? _height : _fallback.Height;
    public uint RowPitch => _isAvailable ? _rowPitch : _fallback.RowPitch;

    public void Render(PackedScene scene, Span<byte> destination)
    {
        if (!_isAvailable)
        {
            EnsureFallbackSize(scene.Uniforms.CanvasW, scene.Uniforms.CanvasH);
            _fallback.Render(scene, destination);
            return;
        }

        EnsureCanvasResources(scene.Uniforms.CanvasW, scene.Uniforms.CanvasH);

        try
        {
            RenderGpu(scene, destination);
        }
        catch
        {
            EnsureFallbackSize(scene.Uniforms.CanvasW, scene.Uniforms.CanvasH);
            _fallback.Render(scene, destination);
        }
    }

    private void RenderGpu(PackedScene scene, Span<byte> destination)
    {
        var shapes = PrepareShapes(scene, out var clipRefCount, out var combinedRefs);
        var uniforms = scene.Uniforms;

        var uniformSpan = MemoryMarshal.CreateReadOnlySpan(ref uniforms, 1);
        using var uniformBuffer = CreateBuffer<UniformsGpu>(uniformSpan, BufferUsage.Uniform);
        using var shapeBuffer = CreateBuffer<ShapeGpu>(shapes.AsSpan(), BufferUsage.Storage);
        using var vertexBuffer = CreateBuffer<float>(scene.Vertices.AsSpan(), BufferUsage.Storage);
        using var tileOcBuffer = CreateBuffer<uint>(scene.TileOffsetCounts.AsSpan(), BufferUsage.Storage);
        using var tileIndexBuffer = CreateBuffer<uint>(scene.TileShapeIndices.AsSpan(), BufferUsage.Storage);
        using var clipBuffer = CreateBuffer<ClipGpu>(scene.Clips.AsSpan(), BufferUsage.Storage);
        using var maskBuffer = CreateBuffer<MaskGpu>(scene.Masks.AsSpan(), BufferUsage.Storage);
        using var refsBuffer = CreateBuffer<uint>(combinedRefs.AsSpan(), BufferUsage.Storage);

        var bindEntries = stackalloc BindGroupEntry[9];
        bindEntries[0] = new BindGroupEntry { Binding = 0, Buffer = uniformBuffer.Pointer, Size = uniformBuffer.Size };
        bindEntries[1] = new BindGroupEntry { Binding = 1, Buffer = shapeBuffer.Pointer, Size = shapeBuffer.Size };
        bindEntries[2] = new BindGroupEntry { Binding = 2, Buffer = vertexBuffer.Pointer, Size = vertexBuffer.Size };
        bindEntries[3] = new BindGroupEntry { Binding = 3, Buffer = tileOcBuffer.Pointer, Size = tileOcBuffer.Size };
        bindEntries[4] = new BindGroupEntry { Binding = 4, Buffer = tileIndexBuffer.Pointer, Size = tileIndexBuffer.Size };
        bindEntries[5] = new BindGroupEntry { Binding = 5, Buffer = clipBuffer.Pointer, Size = clipBuffer.Size };
        bindEntries[6] = new BindGroupEntry { Binding = 6, Buffer = maskBuffer.Pointer, Size = maskBuffer.Size };
        bindEntries[7] = new BindGroupEntry { Binding = 7, Buffer = refsBuffer.Pointer, Size = refsBuffer.Size };
        bindEntries[8] = new BindGroupEntry { Binding = 8, TextureView = _outputView };

        var bindGroupDesc = new BindGroupDescriptor
        {
            Layout = _bindGroupLayout,
            EntryCount = 9,
            Entries = bindEntries
        };

        var bindGroup = _api.DeviceCreateBindGroup(_device, &bindGroupDesc);

        var encoder = _api.DeviceCreateCommandEncoder(_device, null);
        var passDesc = new ComputePassDescriptor();
        var pass = _api.CommandEncoderBeginComputePass(encoder, &passDesc);
        _api.ComputePassEncoderSetPipeline(pass, _computePipeline);
        _api.ComputePassEncoderSetBindGroup(pass, 0, bindGroup, 0, null);
        var groupsX = DivideRoundUp(_width, WorkgroupSize);
        var groupsY = DivideRoundUp(_height, WorkgroupSize);
        _api.ComputePassEncoderDispatchWorkgroups(pass, groupsX, groupsY, 1);
        _api.ComputePassEncoderEnd(pass);

        var src = new ImageCopyTexture
        {
            Texture = _outputTexture,
            Aspect = TextureAspect.All,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, 0)
        };

        var dst = new ImageCopyBuffer
        {
            Buffer = _readbackBuffer,
            Layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = _rowPitch,
                RowsPerImage = _height
            }
        };

        var copySize = new Extent3D(_width, _height, 1);
        _api.CommandEncoderCopyTextureToBuffer(encoder, &src, &dst, &copySize);

        var commandBuffer = _api.CommandEncoderFinish(encoder, null);
        _api.QueueSubmit(_queue, 1, &commandBuffer);
        WaitForQueue();

        Readback(destination);

        _api.CommandBufferRelease(commandBuffer);
        _api.BindGroupRelease(bindGroup);
        _api.CommandEncoderRelease(encoder);
        _api.ComputePassEncoderRelease(pass);
    }

    private static ShapeGpu[] PrepareShapes(PackedScene scene, out uint clipRefCount, out uint[] combinedRefs)
    {
        clipRefCount = (uint)scene.ClipRefs.Length;
        combinedRefs = new uint[scene.ClipRefs.Length + scene.MaskRefs.Length];
        scene.ClipRefs.CopyTo(combinedRefs, 0);
        scene.MaskRefs.CopyTo(combinedRefs, scene.ClipRefs.Length);

        var result = new ShapeGpu[scene.Shapes.Length];
        for (var i = 0; i < scene.Shapes.Length; i++)
        {
            var shape = scene.Shapes[i];
            shape.MaskStart += clipRefCount;
            result[i] = shape;
        }
        return result;
    }

    private void Readback(Span<byte> destination)
    {
        var totalBytes = checked((int)(_rowPitch * _height));
        if (destination.Length < totalBytes)
            throw new ArgumentException("Destination buffer too small.", nameof(destination));

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        _api.BufferMapAsync(_readbackBuffer, MapMode.Read, 0, _readbackSize, new PfnBufferMapCallback(&OnBufferMapped), (void*)GCHandle.ToIntPtr(handle));
        if (!tcs.Task.GetAwaiter().GetResult())
        {
            throw new InvalidOperationException("Failed to map readback buffer.");
        }

        var ptr = _api.BufferGetMappedRange(_readbackBuffer, 0, _readbackSize);
        var mapped = new ReadOnlySpan<byte>(ptr, (int)_readbackSize);
        var stride = (int)_rowPitch;
        for (var y = 0; y < _height; y++)
        {
            var src = mapped.Slice(y * stride, stride);
            var dst = destination.Slice(y * stride, stride);
            src.CopyTo(dst);
        }
        _api.BufferUnmap(_readbackBuffer);
    }

    public void Dispose()
    {
        _fallback.Dispose();
        ReleaseTextureResources();

        if (_bindGroupLayout != null)
        {
            _api.BindGroupLayoutRelease(_bindGroupLayout);
            _bindGroupLayout = null;
        }
        if (_computePipeline != null)
        {
            _api.ComputePipelineRelease(_computePipeline);
            _computePipeline = null;
        }
        if (_computeModule != null)
        {
            _api.ShaderModuleRelease(_computeModule);
            _computeModule = null;
        }
        if (_queue != null)
        {
            _api.QueueRelease(_queue);
            _queue = null;
        }
        if (_device != null)
        {
            _api.DeviceRelease(_device);
            _device = null;
        }
        if (_adapter != null)
        {
            _api.AdapterRelease(_adapter);
            _adapter = null;
        }
        if (_instance != null)
        {
            _api.InstanceRelease(_instance);
            _instance = null;
        }
    }

    private void Initialize()
    {
        var instanceDesc = new InstanceDescriptor();
        _instance = _api.CreateInstance(&instanceDesc);
        if (_instance == null)
            return;

        _adapter = RequestAdapter(_instance);
        if (_adapter == null)
            return;

        _device = RequestDevice(_adapter);
        if (_device == null)
            return;

        _queue = _api.DeviceGetQueue(_device);
        _computeModule = CreateShaderModule(ComputeShaderSource);
        _computePipeline = CreateComputePipeline(_computeModule, "main");
        _bindGroupLayout = _api.ComputePipelineGetBindGroupLayout(_computePipeline, 0);
        _isAvailable = true;
    }

    private void EnsureCanvasResources(uint width, uint height)
    {
        if (!_isAvailable)
            return;

        if (_outputTexture != null && width == _width && height == _height)
            return;

        ReleaseTextureResources();
        _width = width;
        _height = height;
        _rowPitch = Align(width * 4, 256);
        _readbackSize = (nuint)(_rowPitch * height);

        var textureDesc = new TextureDescriptor
        {
            Dimension = TextureDimension.Dimension2D,
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
            Size = new Extent3D(width, height, 1),
            Usage = TextureUsage.TextureBinding | TextureUsage.CopySrc | TextureUsage.StorageBinding
        };

        _outputTexture = _api.DeviceCreateTexture(_device, &textureDesc);
        _outputView = _api.TextureCreateView(_outputTexture, null);

        var bufferDesc = new BufferDescriptor
        {
            Size = _readbackSize,
            Usage = BufferUsage.CopyDst | BufferUsage.MapRead
        };
        _readbackBuffer = _api.DeviceCreateBuffer(_device, &bufferDesc);
    }

    private void ReleaseTextureResources()
    {
        if (_readbackBuffer != null)
        {
            _api.BufferRelease(_readbackBuffer);
            _readbackBuffer = null;
        }
        if (_outputView != null)
        {
            _api.TextureViewRelease(_outputView);
            _outputView = null;
        }
        if (_outputTexture != null)
        {
            _api.TextureRelease(_outputTexture);
            _outputTexture = null;
        }
    }

    private void EnsureFallbackSize(uint width, uint height)
    {
        if (_fallback.Width == width && _fallback.Height == height)
            return;

        _fallback.Dispose();
        _fallback = new CpuFallbackRenderer(width, height);
    }

    private ShaderModule* CreateShaderModule(string source)
    {
        var codePtr = (byte*)SilkMarshal.StringToPtr(source);
        var wgslDesc = new ShaderModuleWGSLDescriptor
        {
            Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
            Code = codePtr
        };
        var shaderDesc = new ShaderModuleDescriptor
        {
            NextInChain = (ChainedStruct*)&wgslDesc
        };

        var module = _api.DeviceCreateShaderModule(_device, &shaderDesc);
        SilkMarshal.Free((nint)codePtr);
        return module;
    }

    private ComputePipeline* CreateComputePipeline(ShaderModule* module, string entryPoint)
    {
        var entryPtr = (byte*)SilkMarshal.StringToPtr(entryPoint);
        var stageDesc = new ProgrammableStageDescriptor
        {
            Module = module,
            EntryPoint = entryPtr
        };
        var pipelineDesc = new ComputePipelineDescriptor
        {
            Compute = stageDesc
        };
        var pipeline = _api.DeviceCreateComputePipeline(_device, &pipelineDesc);
        SilkMarshal.Free((nint)entryPtr);
        return pipeline;
    }

    private Adapter* RequestAdapter(Instance* instance)
    {
        var tcs = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        _api.InstanceRequestAdapter(instance, null, new PfnRequestAdapterCallback(&OnRequestAdapter), (void*)GCHandle.ToIntPtr(handle));
        var result = tcs.Task.GetAwaiter().GetResult();
        return (Adapter*)result;
    }

    private Device* RequestDevice(Adapter* adapter)
    {
        var tcs = new TaskCompletionSource<nint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        _api.AdapterRequestDevice(adapter, null, new PfnRequestDeviceCallback(&OnRequestDevice), (void*)GCHandle.ToIntPtr(handle));
        var result = tcs.Task.GetAwaiter().GetResult();
        return (Device*)result;
    }

    private AutoBuffer CreateBuffer<T>(ReadOnlySpan<T> data, BufferUsage usage) where T : unmanaged
    {
        var byteLength = (nuint)(Unsafe.SizeOf<T>() * data.Length);
        var size = byteLength == 0 ? (nuint)4 : Align(byteLength, 4);
        var descriptor = new BufferDescriptor
        {
            Size = size,
            Usage = usage | BufferUsage.CopyDst
        };

        var buffer = _api.DeviceCreateBuffer(_device, &descriptor);
        if (byteLength > 0)
        {
            fixed (T* src = data)
            {
                _api.QueueWriteBuffer(_queue, buffer, 0, src, byteLength);
            }
        }

        return new AutoBuffer(_api, buffer, size);
    }

    private void WaitForQueue()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = GCHandle.Alloc(tcs);
        _api.QueueOnSubmittedWorkDone(_queue, new PfnQueueWorkDoneCallback(&OnQueueWorkDone), (void*)GCHandle.ToIntPtr(handle));
        if (!tcs.Task.GetAwaiter().GetResult())
        {
            throw new InvalidOperationException("GPU queue work failed.");
        }
    }

    private static uint Align(uint value, uint alignment)
        => (value + alignment - 1) / alignment * alignment;

    private static nuint Align(nuint value, nuint alignment)
        => (value + alignment - 1) / alignment * alignment;

    private static uint DivideRoundUp(uint value, uint divisor)
        => (value + divisor - 1) / divisor;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnRequestAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userData)
    {
        var handle = GCHandle.FromIntPtr((nint)userData);
        var tcs = (TaskCompletionSource<nint>)handle.Target!;
        tcs.SetResult(status == RequestAdapterStatus.Success ? (nint)adapter : nint.Zero);
        handle.Free();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnRequestDevice(RequestDeviceStatus status, Device* device, byte* message, void* userData)
    {
        var handle = GCHandle.FromIntPtr((nint)userData);
        var tcs = (TaskCompletionSource<nint>)handle.Target!;
        tcs.SetResult(status == RequestDeviceStatus.Success ? (nint)device : nint.Zero);
        handle.Free();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnBufferMapped(BufferMapAsyncStatus status, void* userData)
    {
        var handle = GCHandle.FromIntPtr((nint)userData);
        var tcs = (TaskCompletionSource<bool>)handle.Target!;
        tcs.SetResult(status == BufferMapAsyncStatus.Success);
        handle.Free();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnQueueWorkDone(QueueWorkDoneStatus status, void* userData)
    {
        var handle = GCHandle.FromIntPtr((nint)userData);
        var tcs = (TaskCompletionSource<bool>)handle.Target!;
        tcs.SetResult(status == QueueWorkDoneStatus.Success);
        handle.Free();
    }

    private sealed unsafe class AutoBuffer : IDisposable
    {
        private readonly WebGpuApi _api;
        public BufferHandle* Pointer { get; }
        public nuint Size { get; }

        public AutoBuffer(WebGpuApi api, BufferHandle* pointer, nuint size)
        {
            _api = api;
            Pointer = pointer;
            Size = size;
        }

        public void Dispose()
        {
            if (Pointer != null)
            {
                _api.BufferRelease(Pointer);
            }
        }
    }
}
