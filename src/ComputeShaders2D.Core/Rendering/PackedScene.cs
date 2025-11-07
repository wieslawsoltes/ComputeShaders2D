namespace ComputeShaders2D.Core.Rendering;

public sealed class PackedScene
{
    public required ShapeGpu[] Shapes { get; init; }
    public required ClipGpu[] Clips { get; init; }
    public required MaskGpu[] Masks { get; init; }
    public required float[] Vertices { get; init; }
    public required uint[] ClipRefs { get; init; }
    public required uint[] MaskRefs { get; init; }
    public required uint[] TileOffsetCounts { get; init; }
    public required uint[] TileShapeIndices { get; init; }
    public required UniformsGpu Uniforms { get; init; }
    public double TimeSeconds { get; init; }
    public double DeltaSeconds { get; init; }
    public ulong Frame { get; init; }
}
