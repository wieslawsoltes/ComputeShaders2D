using System;
using System.Collections.Generic;
using System.Numerics;
using ComputeShaders2D.Core.Geometry;

namespace ComputeShaders2D.Core.Rendering;

internal static class TextOutliner
{
    public static PathBuilder LayoutText(IFont font, string text, float originX, float originY, float size, TextOptions options)
    {
        var builder = new PathBuilder();
        if (string.IsNullOrEmpty(text))
            return builder;

        var pen = new Vector2(originX, originY + options.BaselineOffset);
        var lineHeight = size * (options.LineSpacing <= 0 ? 1f : options.LineSpacing);

        foreach (var character in text)
        {
            if (character == '\n')
            {
                pen.X = originX;
                pen.Y += lineHeight;
                continue;
            }

            var glyph = font.GetGlyph(character);
            AppendGlyphContours(builder, glyph.Contours, pen, size);
            pen.X += (glyph.Advance * size) + options.LetterSpacing;
        }

        return builder;
    }

    private static void AppendGlyphContours(PathBuilder builder, IReadOnlyList<Vector2[]> contours, Vector2 pen, float size)
    {
        foreach (var contour in contours)
        {
            if (contour.Length == 0)
                continue;

            builder.MoveTo(pen.X + contour[0].X * size, pen.Y + contour[0].Y * size);
            for (var i = 1; i < contour.Length; i++)
            {
                builder.LineTo(pen.X + contour[i].X * size, pen.Y + contour[i].Y * size);
            }
            builder.ClosePath();
        }
    }
}
