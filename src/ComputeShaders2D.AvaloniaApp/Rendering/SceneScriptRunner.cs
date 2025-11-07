using System;
using System.Threading.Tasks;
using ComputeShaders2D.AvaloniaApp.Models;
using ComputeShaders2D.Core.Rendering;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace ComputeShaders2D.AvaloniaApp.Rendering;

internal static class SceneScriptRunner
{
    private static readonly ScriptOptions Options = ScriptOptions.Default
        .AddReferences(typeof(VectorApi).Assembly, typeof(System.Numerics.Vector2).Assembly)
        .AddImports(
            "System",
            "System.Numerics",
            "ComputeShaders2D.Core.Rendering");

    public static bool TryBuild(string script, RenderFrameContext context, RenderSettings settings, IFont? font, out PackedScene? scene, out Exception? error)
    {
        try
        {
            var api = new VectorApi((int)context.Width, (int)context.Height, settings.TileSize, settings.Supersample)
            {
                DefaultFillRule = settings.FillRule,
                TimeSeconds = context.TimeSeconds,
                DeltaSeconds = context.DeltaSeconds,
                Frame = context.FrameIndex
            };

            var globals = new ScriptGlobals(api, context, settings, font ?? FontLoader.GetDefault());
            CSharpScript.RunAsync(script, Options, globals, typeof(ScriptGlobals)).GetAwaiter().GetResult();
            scene = api.BuildScene();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            scene = null;
            error = ex;
            return false;
        }
    }
}

internal sealed class ScriptGlobals
{
    public ScriptGlobals(VectorApi api, RenderFrameContext context, RenderSettings settings, IFont font)
    {
        this.api = api;
        Context = context;
        Settings = settings;
        Font = font;
    }

    public VectorApi api { get; }
    public RenderFrameContext Context { get; }
    public RenderSettings Settings { get; }
    public IFont Font { get; }
    public string SvgPath => SceneAssets.LogoPath;
    public double Time => Context.TimeSeconds;
    public double Dt => Context.DeltaSeconds;
    public ulong Frame => Context.FrameIndex;
}
