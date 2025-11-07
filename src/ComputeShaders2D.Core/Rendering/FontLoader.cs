using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;

namespace ComputeShaders2D.Core.Rendering;

public static class FontLoader
{
    private static readonly IFont DefaultFont;

    static FontLoader()
    {
        DefaultFont = TryCreateSystemFont(SKTypeface.Default?.FamilyName) ?? new SimpleFont("VectorSans");
    }

    public static Task<IFont> LoadAsync(ReadOnlyMemory<byte> fontBytes)
    {
        if (fontBytes.IsEmpty)
            return Task.FromResult(GetDefault());

        try
        {
            using var stream = new SKMemoryStream(fontBytes.ToArray());
            var typeface = SKTypeface.FromStream(stream);
            if (typeface != null)
                return Task.FromResult<IFont>(new SkiaFont(typeface));
        }
        catch
        {
            // ignored - fallback to default
        }

        return Task.FromResult(GetDefault());
    }

    public static Task<IFont> LoadAsync(string familyName)
    {
        var font = TryCreateSystemFont(familyName);
        return Task.FromResult(font ?? GetDefault());
    }

    public static IFont GetDefault() => DefaultFont;

    private static IFont? TryCreateSystemFont(string? familyName)
    {
        try
        {
            SKTypeface? typeface = null;
            if (!string.IsNullOrWhiteSpace(familyName))
            {
                typeface = SKTypeface.FromFamilyName(familyName);
            }

            typeface ??= SKTypeface.Default;
            return typeface is not null ? new SkiaFont(typeface, familyName) : null;
        }
        catch
        {
            return null;
        }
    }
}
