using System.Numerics;

namespace ComputeShaders2D.Core.Rendering;

/// <summary>
/// Helper struct storing an RGBA color as bytes while
/// providing conversion helpers for GPU-friendly formats.
/// </summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A)
{
    public static RgbaColor FromBytes(byte r, byte g, byte b, byte a = 255) => new(r, g, b, a);

    public Vector4 ToPremultipliedVector()
    {
        var alpha = A / 255f;
        return new Vector4(
            alpha > 0 ? (R / 255f) * alpha : 0f,
            alpha > 0 ? (G / 255f) * alpha : 0f,
            alpha > 0 ? (B / 255f) * alpha : 0f,
            alpha);
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}
