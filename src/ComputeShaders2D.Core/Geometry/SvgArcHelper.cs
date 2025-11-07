using System;
using System.Collections.Generic;
using System.Numerics;

namespace ComputeShaders2D.Core.Geometry;

internal static class SvgArcHelper
{
    public static IEnumerable<(Vector2 C1, Vector2 C2, Vector2 P2)> ToCubic(
        Vector2 start,
        Vector2 end,
        float rx,
        float ry,
        float rotationDegrees,
        bool largeArc,
        bool sweep)
    {
        if (MathF.Abs(rx) < 1e-6f || MathF.Abs(ry) < 1e-6f || start == end)
        {
            var chord = (end - start) / 3f;
            yield return (start + chord, end - chord, end);
            yield break;
        }

        var angle = rotationDegrees * (MathF.PI / 180f);
        var cosAngle = MathF.Cos(angle);
        var sinAngle = MathF.Sin(angle);

        var dx2 = (start.X - end.X) / 2f;
        var dy2 = (start.Y - end.Y) / 2f;

        var x1Prime = cosAngle * dx2 + sinAngle * dy2;
        var y1Prime = -sinAngle * dx2 + cosAngle * dy2;

        rx = MathF.Abs(rx);
        ry = MathF.Abs(ry);

        var radiusCheck = x1Prime * x1Prime / (rx * rx) + y1Prime * y1Prime / (ry * ry);
        if (radiusCheck > 1f)
        {
            var scale = MathF.Sqrt(radiusCheck);
            rx *= scale;
            ry *= scale;
        }

        var rxSq = rx * rx;
        var rySq = ry * ry;
        var xPrimeSq = x1Prime * x1Prime;
        var yPrimeSq = y1Prime * y1Prime;

        var numerator = rxSq * rySq - rxSq * yPrimeSq - rySq * xPrimeSq;
        var denominator = rxSq * yPrimeSq + rySq * xPrimeSq;
        var radicant = numerator / denominator;
        if (radicant < 0f) radicant = 0f;
        var coef = (largeArc == sweep ? -1f : 1f) * MathF.Sqrt(radicant);

        var cxPrime = coef * ((rx * y1Prime) / ry);
        var cyPrime = coef * (-(ry * x1Prime) / rx);

        var midX = (start.X + end.X) / 2f;
        var midY = (start.Y + end.Y) / 2f;

        var center = new Vector2(
            cosAngle * cxPrime - sinAngle * cyPrime + midX,
            sinAngle * cxPrime + cosAngle * cyPrime + midY);

        float StartAngle(Vector2 u)
        {
            var v = new Vector2(1f, 0f);
            var dot = HlslClamp(Vector2.Dot(v, u) / (u.Length() + 1e-6f), -1f, 1f);
            var angle = MathF.Acos(dot);
            if (u.Y < 0) angle = -angle;
            return angle;
        }

        float AngleBetween(Vector2 u, Vector2 v)
        {
            var dot = HlslClamp(Vector2.Dot(u, v) / (u.Length() * v.Length() + 1e-6f), -1f, 1f);
            var angle = MathF.Acos(dot);
            if (Cross(u, v) < 0) angle = -angle;
            return angle;
        }

        var unitX = (x1Prime - cxPrime) / rx;
        var unitY = (y1Prime - cyPrime) / ry;
        var startVector = new Vector2(unitX, unitY);
        var theta1 = StartAngle(startVector);

        var endVector = new Vector2((-x1Prime - cxPrime) / rx, (-y1Prime - cyPrime) / ry);
        var deltaTheta = AngleBetween(startVector, endVector);

        if (!sweep && deltaTheta > 0) deltaTheta -= MathF.Tau;
        else if (sweep && deltaTheta < 0) deltaTheta += MathF.Tau;

        var segments = Math.Max(1, (int)Math.Ceiling(MathF.Abs(deltaTheta) / (MathF.PI / 2f)));
        var delta = deltaTheta / segments;

        for (var i = 0; i < segments; i++)
        {
            var t1 = theta1 + i * delta;
            var t2 = t1 + delta;

            var sinT1 = MathF.Sin(t1);
            var cosT1 = MathF.Cos(t1);
            var sinT2 = MathF.Sin(t2);
            var cosT2 = MathF.Cos(t2);

            var factor = (4f / 3f) * MathF.Tan((t2 - t1) / 4f);

            var p1 = PointOnEllipse(cosT1, sinT1);
            var p2 = PointOnEllipse(cosT2, sinT2);
            var c1 = new Vector2(
                p1.X - factor * (cosAngle * rx * sinT1 + sinAngle * ry * cosT1),
                p1.Y - factor * (sinAngle * rx * sinT1 - cosAngle * ry * cosT1));
            var c2 = new Vector2(
                p2.X + factor * (cosAngle * rx * sinT2 + sinAngle * ry * cosT2),
                p2.Y + factor * (sinAngle * rx * sinT2 - cosAngle * ry * cosT2));

            yield return (c1, c2, p2);
        }

        Vector2 PointOnEllipse(float cosT, float sinT)
        {
            return new Vector2(
                cosAngle * rx * cosT - sinAngle * ry * sinT + midX,
                sinAngle * rx * cosT + cosAngle * ry * sinT + midY);
        }
    }

    private static float HlslClamp(float value, float min, float max)
        => MathF.Min(MathF.Max(value, min), max);

    private static float Cross(Vector2 a, Vector2 b)
        => a.X * b.Y - a.Y * b.X;
}
