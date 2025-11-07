using System;
using ComputeShaders2D.Core.Rendering;

namespace ComputeShaders2D.ComputeSharp;

/// <summary>
/// Non-Windows fallback that simply proxies to the CPU renderer while still exposing frame hashes.
/// </summary>
public sealed class ComputeSharpVectorRenderer : IVectorRenderer
{
    private CpuFallbackRenderer _fallback;

    public ComputeSharpVectorRenderer(uint width, uint height)
    {
        Width = width;
        Height = height;
        RowPitch = width * 4;
        _fallback = new CpuFallbackRenderer(width, height);
        IsAvailable = false;
    }

    public bool IsAvailable { get; }

    public uint Width { get; }

    public uint Height { get; }

    public uint RowPitch { get; }

    public ulong LastFrameHash { get; private set; }

    public void Render(PackedScene scene, Span<byte> destination)
    {
        EnsureFallbackSize(scene.Uniforms.CanvasW, scene.Uniforms.CanvasH);
        _fallback.Render(scene, destination);
        LastFrameHash = RendererDiagnostics.ComputeHash(destination.Slice(0, (int)(RowPitch * Height)));
    }

    private void EnsureFallbackSize(uint width, uint height)
    {
        if (_fallback.Width == width && _fallback.Height == height)
            return;

        _fallback.Dispose();
        _fallback = new CpuFallbackRenderer(width, height);
    }

    public void Dispose() => _fallback.Dispose();
}
