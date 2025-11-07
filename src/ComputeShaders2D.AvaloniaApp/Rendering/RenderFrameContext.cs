namespace ComputeShaders2D.AvaloniaApp.Rendering;

public readonly record struct RenderFrameContext(
    uint Width,
    uint Height,
    double TimeSeconds,
    double DeltaSeconds,
    ulong FrameIndex);
