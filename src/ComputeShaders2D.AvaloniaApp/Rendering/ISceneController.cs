using System;
using ComputeShaders2D.Core.Rendering;
using ComputeShaders2D.AvaloniaApp.Models;

namespace ComputeShaders2D.AvaloniaApp.Rendering;

public interface ISceneController
{
    RenderSettings Settings { get; }
    RendererMode RendererMode { get; }
    SceneSampleKind Sample { get; }
    bool IsPlaying { get; }
    bool RandomizeRequested { get; }
    bool ResetTimeRequested { get; }
    string ScriptText { get; }
    RenderStats Stats { get; }
    bool UseScript { get; }

    PackedScene BuildScene(RenderFrameContext context);
    void UpdateStats(double buildMs, double rasterMs, double frameMs, double fps, string tilesText, string modeText);
    void RequestResetTime();
    void RequestRandomizeLines();
    void ClearResetRequest();
    void EnableScript(string source);
    void DisableScript();

    event EventHandler? SceneInvalidated;
}
