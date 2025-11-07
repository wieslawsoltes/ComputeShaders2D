namespace ComputeShaders2D.Core.Rendering;

public interface IVectorRenderer : IDisposable
{
    bool IsAvailable { get; }
    uint Width { get; }
    uint Height { get; }
    uint RowPitch { get; }

    /// <summary>
    /// Renders the provided packed scene into the supplied scratch buffer.
    /// The buffer length must be at least RowPitch * Height bytes.
    /// </summary>
    void Render(PackedScene scene, Span<byte> destination);
}
