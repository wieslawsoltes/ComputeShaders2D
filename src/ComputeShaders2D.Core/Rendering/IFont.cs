using System.Collections.Generic;
using System.Numerics;

namespace ComputeShaders2D.Core.Rendering;

/// <summary>
/// Minimal glyph provider used by <see cref="VectorApi.TextPath"/>.
/// Implementations return contours normalized to the unit square (0..1).
/// </summary>
public interface IFont
{
    string Name { get; }
    GlyphShape GetGlyph(char c);
}

public sealed record GlyphShape(IReadOnlyList<Vector2[]> Contours, float Advance)
{
    public static readonly GlyphShape Empty = new(Array.Empty<Vector2[]>(), 0.6f);
}
