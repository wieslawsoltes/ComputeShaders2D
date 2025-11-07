using ComputeShaders2D.Core.Rendering;

namespace ComputeShaders2D.AvaloniaApp.Models;

public sealed class RenderSettings
{
    public int CanvasWidth { get; set; } = 800;
    public int CanvasHeight { get; set; } = 600;
    public int TileSize { get; set; } = 64;
    public int Supersample { get; set; } = 2;
    public int CpuWorkers { get; set; } = 4;
    public float StrokeWidth { get; set; } = 10f;
    public StrokeJoin StrokeJoin { get; set; } = StrokeJoin.Round;
    public StrokeCap StrokeCap { get; set; } = StrokeCap.Round;
    public float MiterLimit { get; set; } = 4f;
    public FillRule FillRule { get; set; } = FillRule.EvenOdd;
    public bool UseSystemFont { get; set; }
    public string FontFamily { get; set; } = "Segoe UI";
    public bool ShowSvgOverlay { get; set; }
}
