#if WINDOWS
using System;
using ComputeSharp;
using ComputeShaders2D.Core.Rendering;

namespace ComputeShaders2D.ComputeSharp;

/// <summary>
/// Windows/DX12 renderer that executes the WGSL-equivalent compute shader via ComputeSharp.
/// </summary>
public sealed partial class ComputeSharpVectorRenderer : IVectorRenderer
{
    private readonly GraphicsDevice? _device;
    private CpuFallbackRenderer _fallback;

    public ComputeSharpVectorRenderer(uint width, uint height)
    {
        Width = width;
        Height = height;
        RowPitch = width * 4;
        _fallback = new CpuFallbackRenderer(width, height);
        try
        {
            _device = GraphicsDevice.GetDefault();
            IsAvailable = _device is not null;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public bool IsAvailable { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint RowPitch { get; }

    public ulong LastFrameHash { get; private set; }

    public void Render(PackedScene scene, Span<byte> destination)
    {
        if (!IsAvailable || _device is null || scene.Uniforms.CanvasW != Width || scene.Uniforms.CanvasH != Height)
        {
            RenderFallback(scene, destination);
            return;
        }

        try
        {
            RenderGpu(scene, destination);
        }
        catch
        {
            RenderFallback(scene, destination);
        }
    }

    private void RenderFallback(PackedScene scene, Span<byte> destination)
    {
        EnsureFallbackSize(scene.Uniforms.CanvasW, scene.Uniforms.CanvasH);
        _fallback.Render(scene, destination);
        LastFrameHash = RendererDiagnostics.ComputeHash(destination.Slice(0, (int)(RowPitch * Height)));
    }

    private void RenderGpu(PackedScene scene, Span<byte> destination)
    {
        var (shapes, combinedRefs) = PrepareShapes(scene);

        using var shapesBuffer = _device!.AllocateReadOnlyBuffer(shapes);
        using var verticesBuffer = _device.AllocateReadOnlyBuffer(scene.Vertices);
        using var tileOcBuffer = _device.AllocateReadOnlyBuffer(scene.TileOffsetCounts);
        using var tileShapeIndexBuffer = _device.AllocateReadOnlyBuffer(scene.TileShapeIndices);
        using var clipBuffer = _device.AllocateReadOnlyBuffer(scene.Clips);
        using var maskBuffer = _device.AllocateReadOnlyBuffer(scene.Masks);
        using var refsBuffer = _device.AllocateReadOnlyBuffer(combinedRefs);
        using var outputBuffer = _device.AllocateReadWriteBuffer<uint>((int)(Width * Height));

        var tilesY = (int)Math.Ceiling(scene.Uniforms.CanvasH / (double)scene.Uniforms.TileSize);
        var supersample = (int)Math.Max(scene.Uniforms.Supersample, 1u);

        var shader = new VectorRasterizer(
            outputBuffer,
            shapesBuffer,
            verticesBuffer,
            tileOcBuffer,
            tileShapeIndexBuffer,
            clipBuffer,
            maskBuffer,
            refsBuffer,
            (int)Width,
            (int)Height,
            (int)scene.Uniforms.TileSize,
            (int)scene.Uniforms.TilesX,
            tilesY,
            supersample);

        _device.For((int)Width, (int)Height, shader);

        var pixelCount = (int)(Width * Height);
        var pixels = System.Buffers.ArrayPool<uint>.Shared.Rent(pixelCount);
        try
        {
            outputBuffer.CopyTo(pixels.AsSpan(0, pixelCount));
            WritePixels(destination, pixels);
        }
        finally
        {
            System.Buffers.ArrayPool<uint>.Shared.Return(pixels);
        }
        LastFrameHash = RendererDiagnostics.ComputeHash(destination.Slice(0, (int)(RowPitch * Height)));
    }

    private void EnsureFallbackSize(uint width, uint height)
    {
        if (_fallback.Width == width && _fallback.Height == height)
            return;

        _fallback.Dispose();
        _fallback = new CpuFallbackRenderer(width, height);
    }

    private void WritePixels(Span<byte> destination, uint[] pixels)
    {
        for (var y = 0; y < Height; y++)
        {
            var src = new ReadOnlySpan<uint>(pixels, (int)(y * Width), (int)Width);
            var dst = destination.Slice((int)(y * RowPitch), (int)(Width * 4));
            System.Runtime.InteropServices.MemoryMarshal.Cast<uint, byte>(src).CopyTo(dst);
        }
    }

    private static (ShapeGpu[] Shapes, uint[] CombinedRefs) PrepareShapes(PackedScene scene)
    {
        var combinedRefs = new uint[scene.ClipRefs.Length + scene.MaskRefs.Length];
        scene.ClipRefs.CopyTo(combinedRefs, 0);
        scene.MaskRefs.CopyTo(combinedRefs, scene.ClipRefs.Length);

        var adjusted = new ShapeGpu[scene.Shapes.Length];
        var clipRefCount = (uint)scene.ClipRefs.Length;
        for (var i = 0; i < scene.Shapes.Length; i++)
        {
            var shape = scene.Shapes[i];
            shape.MaskStart += clipRefCount;
            adjusted[i] = shape;
        }

        return (adjusted, combinedRefs);
    }

    [ThreadGroupSize(8, 8, 1)]
    [GeneratedComputeShaderDescriptor]
    private readonly partial struct VectorRasterizer : IComputeShader
    {
        public readonly ReadWriteBuffer<uint> Output;
        public readonly ReadOnlyBuffer<ShapeGpu> Shapes;
        public readonly ReadOnlyBuffer<float> Vertices;
        public readonly ReadOnlyBuffer<uint> TileOffsetCounts;
        public readonly ReadOnlyBuffer<uint> TileShapeIndices;
        public readonly ReadOnlyBuffer<ClipGpu> Clips;
        public readonly ReadOnlyBuffer<MaskGpu> Masks;
        public readonly ReadOnlyBuffer<uint> Refs;
        public readonly int CanvasWidth;
        public readonly int CanvasHeight;
        public readonly int TileSize;
        public readonly int TilesX;
        public readonly int TilesY;
        public readonly int Supersample;

        public VectorRasterizer(
            ReadWriteBuffer<uint> output,
            ReadOnlyBuffer<ShapeGpu> shapes,
            ReadOnlyBuffer<float> vertices,
            ReadOnlyBuffer<uint> tileOffsetCounts,
            ReadOnlyBuffer<uint> tileShapeIndices,
            ReadOnlyBuffer<ClipGpu> clips,
            ReadOnlyBuffer<MaskGpu> masks,
            ReadOnlyBuffer<uint> refs,
            int canvasWidth,
            int canvasHeight,
            int tileSize,
            int tilesX,
            int tilesY,
            int supersample)
        {
            Output = output;
            Shapes = shapes;
            Vertices = vertices;
            TileOffsetCounts = tileOffsetCounts;
            TileShapeIndices = tileShapeIndices;
            Clips = clips;
            Masks = masks;
            Refs = refs;
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            TileSize = tileSize;
            TilesX = tilesX;
            TilesY = tilesY;
            Supersample = supersample;
        }

        public void Execute()
        {
            int x = ThreadIds.X;
            int y = ThreadIds.Y;
            if (x >= CanvasWidth || y >= CanvasHeight)
                return;

            int tileX = Hlsl.Clamp(x / TileSize, 0, TilesX - 1);
            int tileY = Hlsl.Clamp(y / TileSize, 0, TilesY - 1);
            int tileIndex = tileY * TilesX + tileX;
            int meta = tileIndex * 2;
            uint start = TileOffsetCounts[meta];
            uint count = TileOffsetCounts[meta + 1];

            float4 accum = 0f;
            int ss = Hlsl.Max(1, Supersample);
            float samples = ss * ss;

            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                float2 sample = new(
                    x + ((sx + 0.5f) / ss),
                    y + ((sy + 0.5f) / ss));

                float4 color = 0f;
                for (uint k = 0; k < count; k++)
                {
                    var shapeIndex = TileShapeIndices[(int)(start + k)];
                    var shape = Shapes[(int)shapeIndex];
                    if (!InsidePath(shape.VStart, shape.VCount, shape.Rule, sample))
                        continue;

                    bool insideClips = true;
                    for (uint c = 0; c < shape.ClipCount; c++)
                    {
                        uint clipId = Refs[(int)(shape.ClipStart + c)];
                        var clip = Clips[(int)clipId];
                        if (!InsidePath(clip.VStart, clip.VCount, clip.Rule, sample))
                        {
                            insideClips = false;
                            break;
                        }
                    }
                    if (!insideClips)
                        continue;

                    float maskValue = 1f;
                    if (shape.MaskCount > 0)
                    {
                        maskValue = 0f;
                        for (uint m = 0; m < shape.MaskCount; m++)
                        {
                            uint maskId = Refs[(int)(shape.MaskStart + m)];
                            var mask = Masks[(int)maskId];
                            if (InsidePath(mask.VStart, mask.VCount, mask.Rule, sample))
                            {
                                maskValue = maskValue + (1f - maskValue) * Hlsl.Clamp(mask.Alpha, 0f, 1f);
                            }
                        }
                    }

                    float factor = shape.Opacity * maskValue;
                    if (factor > 0.00001f)
                    {
                        float4 src = new(
                            shape.ColorR * factor,
                            shape.ColorG * factor,
                            shape.ColorB * factor,
                            shape.ColorA * factor);
                        color = Over(src, color);
                    }
                }

                accum += color;
            }

            float4 avg = accum / samples;
            float a = Hlsl.Clamp(avg.W, 0f, 1f);
            float3 rgb = a > 0.00001f ? Hlsl.Saturate(avg.XYZ / a) : 0f;

            uint packed =
                ((uint)Hlsl.Round(a * 255f) << 24) |
                ((uint)Hlsl.Round(rgb.Z * 255f) << 16) |
                ((uint)Hlsl.Round(rgb.Y * 255f) << 8) |
                (uint)Hlsl.Round(rgb.X * 255f);

            int index = y * CanvasWidth + x;
            Output[index] = packed;
        }

        private float2 LoadVertex(uint index)
        {
            int baseIndex = (int)(index * 2);
            return new float2(Vertices[baseIndex], Vertices[baseIndex + 1]);
        }

        private bool InsidePath(uint start, uint count, FillRule rule, float2 point)
            => rule == FillRule.EvenOdd
                ? InsideEvenOdd(start, count, point)
                : InsideNonZero(start, count, point);

        private bool InsideEvenOdd(uint start, uint count, float2 point)
        {
            bool inside = false;
            float2 prev = LoadVertex(start + count - 1);
            for (uint i = 0; i < count; i++)
            {
                float2 curr = LoadVertex(start + i);
                bool cond = ((curr.Y > point.Y) != (prev.Y > point.Y)) &&
                            (point.X < (prev.X - curr.X) * (point.Y - curr.Y) / (prev.Y - curr.Y + 1e-6f) + curr.X);
                if (cond)
                    inside = !inside;
                prev = curr;
            }
            return inside;
        }

        private bool InsideNonZero(uint start, uint count, float2 point)
        {
            int winding = 0;
            float2 prev = LoadVertex(start + count - 1);
            for (uint i = 0; i < count; i++)
            {
                float2 curr = LoadVertex(start + i);
                if (curr.Y <= point.Y)
                {
                    if (prev.Y > point.Y && IsLeft(curr, prev, point) > 0f)
                        winding++;
                }
                else
                {
                    if (prev.Y <= point.Y && IsLeft(curr, prev, point) < 0f)
                        winding--;
                }
                prev = curr;
            }
            return winding != 0;
        }

        private static float IsLeft(float2 a, float2 b, float2 p)
            => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

        private static float4 Over(float4 src, float4 dst)
        {
            float ida = 1f - src.W;
            return new float4(src.XYZ + ida * dst.XYZ, src.W + ida * dst.W);
        }
    }
}
#endif
