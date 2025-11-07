using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace ComputeShaders2D.Core.Geometry;

/// <summary>
/// Mutable builder for complex paths. The API mirrors the JS playground version
/// but keeps data in a command list so it can be flattened later with a configurable tolerance.
/// </summary>
public sealed class PathBuilder
{
    private readonly List<PathCommand> _commands = new();
    private Matrix3x2 _transform = Matrix3x2.Identity;

    public PathBuilder MoveTo(float x, float y)
    {
        _commands.Add(new MoveToCommand(new Vector2(x, y)));
        return this;
    }

    public PathBuilder LineTo(float x, float y)
    {
        _commands.Add(new LineToCommand(new Vector2(x, y)));
        return this;
    }

    public PathBuilder QuadTo(float cpx, float cpy, float x, float y)
    {
        _commands.Add(new QuadraticToCommand(new Vector2(cpx, cpy), new Vector2(x, y)));
        return this;
    }

    public PathBuilder BezierTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
    {
        _commands.Add(new CubicToCommand(new Vector2(c1x, c1y), new Vector2(c2x, c2y), new Vector2(x, y)));
        return this;
    }

    public PathBuilder Arc(float cx, float cy, float radius, float startAngle, float endAngle, bool counterClockwise = false, int segments = 0)
    {
        _commands.Add(new ArcCommand(new Vector2(cx, cy), radius, startAngle, endAngle, counterClockwise, segments));
        return this;
    }

    public PathBuilder Ellipse(float cx, float cy, float rx, float ry, float rotation = 0f, int segments = 64)
    {
        _commands.Add(new EllipseCommand(new Vector2(cx, cy), new Vector2(rx, ry), rotation, segments));
        return this;
    }

    public PathBuilder Poly(IEnumerable<Vector2> points, bool close = false)
    {
        Vector2 first = default;
        var hasFirst = false;
        foreach (var point in points)
        {
            if (!hasFirst)
            {
                MoveTo(point.X, point.Y);
                first = point;
                hasFirst = true;
            }
            else
            {
                LineTo(point.X, point.Y);
            }
        }

        if (close && hasFirst)
        {
            LineTo(first.X, first.Y);
            ClosePath();
        }
        return this;
    }

    public PathBuilder Rect(float x, float y, float width, float height)
    {
        MoveTo(x, y);
        LineTo(x + width, y);
        LineTo(x + width, y + height);
        LineTo(x, y + height);
        ClosePath();
        return this;
    }

    public PathBuilder ClosePath()
    {
        _commands.Add(new CloseCommand());
        return this;
    }

    public PathBuilder Transform(float translateX = 0f, float translateY = 0f, float scaleX = 1f, float scaleY = 1f, float rotate = 0f)
    {
        var translation = Matrix3x2.CreateTranslation(translateX, translateY);
        var scale = Matrix3x2.CreateScale(scaleX, scaleY);
        var rotation = Matrix3x2.CreateRotation(rotate);
        _transform = scale * rotation * translation * _transform;
        return this;
    }

    /// <summary>
    /// Flattens the command list into closed polylines using the provided tolerance.
    /// </summary>
    public IReadOnlyList<Vector2[]> Flatten(float tolerance = 0.35f)
    {
        var result = new List<Vector2[]>();
        var current = new List<Vector2>();
        var currentPos = Vector2.Zero;
        var subPathStart = Vector2.Zero;
        var hasCurrent = false;
        var closeRequested = false;

        void Flush()
        {
            if (current.Count > 1)
            {
                if (closeRequested)
                {
                    var first = current[0];
                    var last = current[^1];
                    if (!Vector2Extensions.NearlyEquals(first, last))
                    {
                        current.Add(first);
                    }
                }

                var poly = current.Select(p => Vector2.Transform(p, _transform)).ToArray();
                result.Add(poly);
            }

            current.Clear();
            closeRequested = false;
            hasCurrent = false;
        }

        foreach (var command in _commands)
        {
            switch (command)
            {
                case MoveToCommand move:
                    Flush();
                    currentPos = move.Point;
                    subPathStart = move.Point;
                    current.Add(move.Point);
                    hasCurrent = true;
                    break;

                case LineToCommand line:
                    EnsureCurrent();
                    current.Add(line.Point);
                    currentPos = line.Point;
                    break;

                case QuadraticToCommand quad:
                    EnsureCurrent();
                    FlattenQuadratic(current, currentPos, quad.Control, quad.Point, tolerance);
                    currentPos = quad.Point;
                    break;

                case CubicToCommand cubic:
                    EnsureCurrent();
                    FlattenCubic(current, currentPos, cubic.Control1, cubic.Control2, cubic.Point, tolerance);
                    currentPos = cubic.Point;
                    break;

                case ArcCommand arc:
                    EnsureCurrent();
                    FlattenArc(current, arc, ref currentPos);
                    break;

                case EllipseCommand ellipse:
                    EnsureCurrent();
                    FlattenEllipse(current, ellipse, ref currentPos);
                    break;

                case CloseCommand:
                    closeRequested = true;
                    Flush();
                    currentPos = subPathStart;
                    break;
            }
        }

        Flush();
        return result;

        void EnsureCurrent()
        {
            if (!hasCurrent)
            {
                current.Add(currentPos);
                hasCurrent = true;
            }
        }
    }

    private static void FlattenQuadratic(List<Vector2> output, Vector2 p0, Vector2 cp, Vector2 p1, float tolerance)
    {
        var stack = new Stack<(Vector2 P0, Vector2 P1, Vector2 P2, int Depth)>();
        stack.Push((p0, cp, p1, 0));

        while (stack.Count > 0)
        {
            var (a, b, c, depth) = stack.Pop();
            var midAB = (a + b) * 0.5f;
            var midBC = (b + c) * 0.5f;
            var mid = (midAB + midBC) * 0.5f;
            var chordMid = (a + c) * 0.5f;
            if ((mid - chordMid).LengthSquared() <= tolerance * tolerance || depth > 10)
            {
                output.Add(c);
                continue;
            }

            stack.Push((mid, midBC, c, depth + 1));
            stack.Push((a, midAB, mid, depth + 1));
        }
    }

    private static void FlattenCubic(List<Vector2> output, Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p1, float tolerance)
    {
        var stack = new Stack<(Vector2 P0, Vector2 C1, Vector2 C2, Vector2 P1, int Depth)>();
        stack.Push((p0, c1, c2, p1, 0));
        var tolSq = tolerance * tolerance * 4f;

        while (stack.Count > 0)
        {
            var (a, b, c, d, depth) = stack.Pop();
            var ab = (a + b) * 0.5f;
            var bc = (b + c) * 0.5f;
            var cd = (c + d) * 0.5f;
            var abbc = (ab + bc) * 0.5f;
            var bccd = (bc + cd) * 0.5f;
            var mid = (abbc + bccd) * 0.5f;

            var chordMid = (a + d) * 0.5f;
            if ((mid - chordMid).LengthSquared() <= tolSq || depth > 10)
            {
                output.Add(d);
                continue;
            }

            stack.Push((mid, bccd, cd, d, depth + 1));
            stack.Push((a, ab, abbc, mid, depth + 1));
        }
    }

    private static void FlattenArc(List<Vector2> output, ArcCommand arc, ref Vector2 currentPos)
    {
        var sweep = arc.EndAngle - arc.StartAngle;
        if (arc.CounterClockwise && sweep < 0) sweep += MathF.Tau;
        if (!arc.CounterClockwise && sweep > 0) sweep -= MathF.Tau;

        var segments = arc.Segments > 0
            ? arc.Segments
            : Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) / (MathF.PI / 10f)), 8, 128);

        for (var i = 1; i <= segments; i++)
        {
            var t = arc.StartAngle + sweep * (i / (float)segments);
            var point = arc.Center + new Vector2(MathF.Cos(t), MathF.Sin(t)) * arc.Radius;
            output.Add(point);
            currentPos = point;
        }
    }

    private static void FlattenEllipse(List<Vector2> output, EllipseCommand ellipse, ref Vector2 currentPos)
    {
        var segments = Math.Clamp(ellipse.Segments, 8, 256);
        var rot = Matrix3x2.CreateRotation(ellipse.Rotation);
        for (var i = 1; i <= segments; i++)
        {
            var t = MathF.Tau * (i / (float)segments);
            var local = new Vector2(MathF.Cos(t) * ellipse.Radii.X, MathF.Sin(t) * ellipse.Radii.Y);
            var rotated = Vector2.Transform(local, rot) + ellipse.Center;
            output.Add(rotated);
            currentPos = rotated;
        }
    }
}

internal static class Vector2Extensions
{
    public static bool NearlyEquals(this Vector2 a, Vector2 b, float eps = 1e-4f)
        => (a - b).LengthSquared() <= eps * eps;
}
