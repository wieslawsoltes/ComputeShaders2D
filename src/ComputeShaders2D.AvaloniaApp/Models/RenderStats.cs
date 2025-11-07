namespace ComputeShaders2D.AvaloniaApp.Models;

public sealed class RenderStats
{
    public double BuildMs { get; set; }
    public double RasterMs { get; set; }
    public double FrameMs { get; set; }
    public double Fps { get; set; }
    public string TilesText { get; set; } = "–";
    public string ModeText { get; set; } = "Static";
}
