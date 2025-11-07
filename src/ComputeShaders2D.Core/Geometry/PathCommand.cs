using System.Numerics;

namespace ComputeShaders2D.Core.Geometry;

internal abstract record PathCommand;

internal sealed record MoveToCommand(Vector2 Point) : PathCommand;
internal sealed record LineToCommand(Vector2 Point) : PathCommand;
internal sealed record QuadraticToCommand(Vector2 Control, Vector2 Point) : PathCommand;
internal sealed record CubicToCommand(Vector2 Control1, Vector2 Control2, Vector2 Point) : PathCommand;
internal sealed record CloseCommand() : PathCommand;
internal sealed record ArcCommand(Vector2 Center, float Radius, float StartAngle, float EndAngle, bool CounterClockwise, int Segments) : PathCommand;
internal sealed record EllipseCommand(Vector2 Center, Vector2 Radii, float Rotation, int Segments) : PathCommand;
