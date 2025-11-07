using System.Numerics;

namespace ComputeShaders2D.Core.Rendering;

/// <summary>
/// Extremely simple CPU renderer used until GPU integration is fully wired.
/// It currently clears the buffer with a solid color derived from the first shape.
/// </summary>
public sealed class CpuFallbackRenderer : IVectorRenderer
{
    public CpuFallbackRenderer(uint width, uint height)
    {
        Width = width;
        Height = height;
        RowPitch = Width * 4;
    }

    public bool IsAvailable => true;
    public uint Width { get; }
    public uint Height { get; }
    public uint RowPitch { get; }

    public void Render(PackedScene scene, Span<byte> destination)
    {
        if (destination.Length < RowPitch * Height)
        {
            throw new ArgumentException("Destination buffer too small.", nameof(destination));
        }

        var color = scene.Shapes.Length > 0
            ? new Vector4(scene.Shapes[0].ColorR, scene.Shapes[0].ColorG, scene.Shapes[0].ColorB, scene.Shapes[0].ColorA)
            : new Vector4(0.1f, 0.12f, 0.18f, 1f);

        var r = (byte)Math.Clamp(color.X * 255f, 0f, 255f);
        var g = (byte)Math.Clamp(color.Y * 255f, 0f, 255f);
        var b = (byte)Math.Clamp(color.Z * 255f, 0f, 255f);
        var a = (byte)Math.Clamp(color.W * 255f, 0f, 255f);

        var stride = destination.Length / (int)Height;
        var rowBytes = (int)(Width * 4);

        for (var y = 0; y < Height; y++)
        {
            var row = destination.Slice(y * stride, stride);
            for (var offset = 0; offset < Math.Min(rowBytes, row.Length); offset += 4)
            {
                row[offset + 0] = r;
                row[offset + 1] = g;
                row[offset + 2] = b;
                row[offset + 3] = a;
            }
        }
    }

    public void Dispose()
    {
    }
}
