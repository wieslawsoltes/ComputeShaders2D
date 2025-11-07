namespace ComputeShaders2D.AvaloniaApp.Models;

public sealed record CanvasSizeOption(string Label, int Width, int Height)
{
    public override string ToString() => Label;
}
