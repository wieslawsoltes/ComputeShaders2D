using System.Collections.Generic;
using System.Numerics;

namespace ComputeShaders2D.Core.Rendering;

internal sealed class SimpleFont : IFont
{
    private readonly Dictionary<char, GlyphShape> _glyphs = new();

    public SimpleFont(string name)
    {
        Name = name;
        BuildGlyphs();
    }

    public string Name { get; }

    public GlyphShape GetGlyph(char c)
    {
        if (_glyphs.TryGetValue(c, out var glyph))
            return glyph;
        return _glyphs['?'];
    }

    private void BuildGlyphs()
    {
        // Basic latin rectangles. Width is proportionally scaled so text looks reasonable.
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        {
            _glyphs[c] = BuildRectGlyph(0.6f);
            _glyphs[char.ToLowerInvariant(c)] = _glyphs[c];
        }

        _glyphs[' '] = new GlyphShape(Array.Empty<Vector2[]>(), 0.4f);
        _glyphs['.'] = BuildDotGlyph();
        _glyphs[','] = BuildDotGlyph(offsetY: 0.2f);
        _glyphs['-'] = BuildBarGlyph();
        _glyphs['?'] = BuildRectGlyph(0.6f);
    }

    private static GlyphShape BuildRectGlyph(float width)
    {
        var contour = new[]
        {
            new Vector2(0, 0),
            new Vector2(width, 0),
            new Vector2(width, 1f),
            new Vector2(0, 1f),
            new Vector2(0, 0)
        };
        return new GlyphShape(new[] { contour }, width * 1.05f);
    }

    private static GlyphShape BuildDotGlyph(float offsetY = 0.75f)
    {
        var size = 0.15f;
        var contour = new[]
        {
            new Vector2(0.2f, offsetY),
            new Vector2(0.2f + size, offsetY),
            new Vector2(0.2f + size, offsetY + size),
            new Vector2(0.2f, offsetY + size),
            new Vector2(0.2f, offsetY)
        };
        return new GlyphShape(new[] { contour }, 0.35f);
    }

    private static GlyphShape BuildBarGlyph()
    {
        var contour = new[]
        {
            new Vector2(0, 0.45f),
            new Vector2(0.6f, 0.45f),
            new Vector2(0.6f, 0.55f),
            new Vector2(0, 0.55f),
            new Vector2(0, 0.45f)
        };
        return new GlyphShape(new[] { contour }, 0.6f);
    }
}
