using System;
using System.Collections.Generic;
using System.Numerics;
using ComputeShaders2D.Core.Rendering;

namespace ComputeShaders2D.Core.Geometry;

internal static class StrokeRasterizer
{
    private const float Epsilon = 1e-5f;

    public static IEnumerable<Vector2[]> BuildStroke(IReadOnlyList<Vector2> polyline, float width, StrokeStyle style)
    {
        if (polyline.Count < 2 || width <= 0f)
            yield break;

        var points = NormalizePolyline(polyline);
        if (points.Count < 2)
            yield break;

        var closed = Vector2Extensions.NearlyEquals(points[0], points[^1]);
        if (closed && points.Count > 2)
        {
            // Remove redundant duplicate that closes the path.
            points.RemoveAt(points.Count - 1);
        }

        var segments = BuildSegments(points, closed);
        if (segments.Count == 0)
            yield break;

        var half = width * 0.5f;

        foreach (var segment in segments)
        {
            var quad = BuildSegmentQuad(segment, half);
            if (quad is not null)
                yield return quad;
        }

        // Joins
        if (segments.Count > 1)
        {
            if (closed)
            {
                for (var i = 0; i < segments.Count; i++)
                {
                    var prev = segments[(i - 1 + segments.Count) % segments.Count];
                    var next = segments[i];
                    var join = BuildJoinPolygon(prev, next, half, style.Join, style.MiterLimit);
                    if (join is not null)
                        yield return join;
                }
            }
            else
            {
                for (var i = 1; i < segments.Count; i++)
                {
                    var prev = segments[i - 1];
                    var next = segments[i];
                    var join = BuildJoinPolygon(prev, next, half, style.Join, style.MiterLimit);
                    if (join is not null)
                        yield return join;
                }
            }
        }

        if (!closed)
        {
            var startCap = BuildCapPolygon(segments[0], half, style.Cap, isStart: true);
            if (startCap is not null)
                yield return startCap;

            var endCap = BuildCapPolygon(segments[^1], half, style.Cap, isStart: false);
            if (endCap is not null)
                yield return endCap;
        }
    }

    private static List<Vector2> NormalizePolyline(IReadOnlyList<Vector2> points)
    {
        var result = new List<Vector2>(points.Count);
        Vector2? previous = null;
        foreach (var point in points)
        {
            if (previous is { } prev && Vector2Extensions.NearlyEquals(prev, point))
                continue;
            result.Add(point);
            previous = point;
        }
        return result;
    }

    private static List<StrokeSegment> BuildSegments(IReadOnlyList<Vector2> points, bool closed)
    {
        var segments = new List<StrokeSegment>();
        var count = points.Count;
        var limit = closed ? count : count - 1;
        for (var i = 0; i < limit; i++)
        {
            var p0 = points[i];
            var p1 = points[(i + 1) % count];
            var delta = p1 - p0;
            if (delta.LengthSquared() < Epsilon)
                continue;

            var direction = Vector2.Normalize(delta);
            var leftNormal = new Vector2(-direction.Y, direction.X);
            segments.Add(new StrokeSegment(p0, p1, direction, leftNormal));
        }
        return segments;
    }

    private static Vector2[]? BuildSegmentQuad(StrokeSegment segment, float half)
    {
        var offset = segment.LeftNormal * half;
        var p0 = segment.Point0;
        var p1 = segment.Point1;
        var points = new[]
        {
            p0 + offset,
            p1 + offset,
            p1 - offset,
            p0 - offset,
            p0 + offset
        };
        return points;
    }

    private static Vector2[]? BuildJoinPolygon(StrokeSegment prev, StrokeSegment next, float half, StrokeJoin join, float miterLimit)
    {
        var cross = Cross(prev.Direction, next.Direction);
        if (MathF.Abs(cross) < Epsilon)
            return null;

        var outerNormalPrev = cross > 0 ? prev.LeftNormal : -prev.LeftNormal;
        var outerNormalNext = cross > 0 ? next.LeftNormal : -next.LeftNormal;
        var point = next.Point0;

        return join switch
        {
            StrokeJoin.Round => BuildArc(point, outerNormalPrev, outerNormalNext, half, cross > 0),
            StrokeJoin.Miter => BuildMiterJoin(point, prev, next, outerNormalPrev, outerNormalNext, half, miterLimit) 
                                ?? BuildBevelJoin(point, outerNormalPrev, outerNormalNext, half),
            _ => BuildBevelJoin(point, outerNormalPrev, outerNormalNext, half)
        };
    }

    private static Vector2[]? BuildMiterJoin(Vector2 center, StrokeSegment prev, StrokeSegment next, Vector2 normalPrev, Vector2 normalNext, float half, float miterLimit)
    {
        if (!TryIntersect(center + normalPrev * half, prev.Direction, center + normalNext * half, next.Direction, out var intersection))
            return null;

        var miterLength = Vector2.Distance(center, intersection);
        if (miterLength > half * MathF.Max(1f, miterLimit))
            return null;

        return new[]
        {
            center,
            center + normalPrev * half,
            intersection,
            center + normalNext * half,
            center
        };
    }

    private static Vector2[] BuildBevelJoin(Vector2 center, Vector2 normalPrev, Vector2 normalNext, float half)
        => new[]
        {
            center,
            center + normalPrev * half,
            center + normalNext * half,
            center
        };

    private static Vector2[]? BuildCapPolygon(StrokeSegment segment, float half, StrokeCap cap, bool isStart)
    {
        switch (cap)
        {
            case StrokeCap.Butt:
                return null;
            case StrokeCap.Square:
                return BuildSquareCap(segment, half, isStart);
            case StrokeCap.Round:
                var normalFrom = isStart ? -segment.LeftNormal : segment.LeftNormal;
                var normalTo = isStart ? segment.LeftNormal : -segment.LeftNormal;
                var center = isStart ? segment.Point0 : segment.Point1;
                return BuildArc(center, normalFrom, normalTo, half, isStart);
            default:
                return null;
        }
    }

    private static Vector2[] BuildSquareCap(StrokeSegment segment, float half, bool isStart)
    {
        var center = isStart ? segment.Point0 : segment.Point1;
        var offset = (isStart ? -segment.Direction : segment.Direction) * half;
        var left = segment.LeftNormal * half;
        var points = new[]
        {
            center + left + offset,
            center + left,
            center - left,
            center - left + offset,
            center + left + offset
        };
        return points;
    }

    private static Vector2[] BuildArc(Vector2 center, Vector2 fromNormal, Vector2 toNormal, float radius, bool isLeft)
    {
        var start = MathF.Atan2(fromNormal.Y, fromNormal.X);
        var end = MathF.Atan2(toNormal.Y, toNormal.X);
        if (isLeft && end < start)
            end += MathF.Tau;
        if (!isLeft && end > start)
            end -= MathF.Tau;

        var sweep = end - start;
        var steps = Math.Max(2, (int)(MathF.Abs(sweep) / (MathF.PI / 12f)));
        var points = new List<Vector2>(steps + 3)
        {
            center,
            center + fromNormal * radius
        };

        for (var i = 1; i < steps; i++)
        {
            var t = i / (float)steps;
            var angle = start + sweep * t;
            points.Add(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius);
        }

        points.Add(center + toNormal * radius);
        points.Add(center);
        return points.ToArray();
    }

    private static bool TryIntersect(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2, out Vector2 intersection)
    {
        var cross = Cross(d1, d2);
        if (MathF.Abs(cross) < Epsilon)
        {
            intersection = default;
            return false;
        }

        var t = Cross(p2 - p1, d2) / cross;
        intersection = p1 + d1 * t;
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private readonly struct StrokeSegment
    {
        public StrokeSegment(Vector2 point0, Vector2 point1, Vector2 direction, Vector2 leftNormal)
        {
            Point0 = point0;
            Point1 = point1;
            Direction = direction;
            LeftNormal = leftNormal;
        }

        public Vector2 Point0 { get; }
        public Vector2 Point1 { get; }
        public Vector2 Direction { get; }
        public Vector2 LeftNormal { get; }
    }
}
