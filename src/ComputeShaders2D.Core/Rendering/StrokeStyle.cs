namespace ComputeShaders2D.Core.Rendering;

public enum StrokeJoin
{
    Round,
    Bevel,
    Miter
}

public enum StrokeCap
{
    Round,
    Butt,
    Square
}

public readonly record struct StrokeStyle(
    StrokeJoin Join,
    StrokeCap Cap,
    float MiterLimit)
{
    public static StrokeStyle Default { get; } = new(StrokeJoin.Round, StrokeCap.Round, 4f);
}
