namespace ComputeShaders2D.Core.Rendering;

/// <summary>
/// Optional authoring tweaks used when laying out text with <see cref="VectorApi"/>.
/// The defaults aim to keep behaviour predictable even without real font metrics.
/// </summary>
public readonly record struct TextOptions(
    float LetterSpacing = 0f,
    float LineSpacing = 1.25f,
    float BaselineOffset = 0f)
{
    public static TextOptions Default { get; } = new();
}
