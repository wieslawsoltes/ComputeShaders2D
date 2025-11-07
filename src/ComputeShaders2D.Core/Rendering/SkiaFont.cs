using System;
using System.Numerics;
using ComputeShaders2D.Core.Geometry;
using SkiaSharp;

namespace ComputeShaders2D.Core.Rendering;

internal sealed class SkiaFont : IFont, IDisposable
{
    private readonly SKTypeface _typeface;
    private readonly SKFont _font;

    public SkiaFont(SKTypeface typeface, string? name = null)
    {
        _typeface = typeface;
        _font = new SKFont(_typeface, 1f)
        {
            Hinting = SKFontHinting.Full,
            Subpixel = true
        };
        Name = name ?? typeface.FamilyName ?? "SkiaFont";
    }

    public string Name { get; }

    public GlyphShape GetGlyph(char c)
    {
        Span<char> codepoints = stackalloc char[1] { c };
        Span<ushort> glyphs = stackalloc ushort[1];
        _font.GetGlyphs(codepoints, glyphs);

        var glyphId = glyphs[0];
        if (glyphId == 0)
            return GlyphShape.Empty;

        using var path = _font.GetGlyphPath(glyphId);
        if (path is null)
            return GlyphShape.Empty;

        var svgData = path.ToSvgPathData();
        var glyphPath = SvgPathParser.Parse(svgData);
        var contours = glyphPath.Flatten();

        Span<float> widths = stackalloc float[1];
        _font.GetGlyphWidths(glyphs, widths, Span<SKRect>.Empty);
        var advance = widths[0];
        if (MathF.Abs(advance) < 1e-4f)
            advance = 0.6f;

        return new GlyphShape(contours, advance);
    }

    public void Dispose()
    {
        _font.Dispose();
        _typeface.Dispose();
    }
}
