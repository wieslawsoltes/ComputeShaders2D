namespace ComputeShaders2D.Core.Rendering;

/// <summary>
/// Fill rule used when testing point containment.
/// Matches the GPU layout (0 = even-odd, 1 = non-zero).
/// </summary>
public enum FillRule : uint
{
    EvenOdd = 0,
    NonZero = 1
}
