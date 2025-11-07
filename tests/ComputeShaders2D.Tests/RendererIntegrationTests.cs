using System;
using ComputeShaders2D.ComputeSharp;
using ComputeShaders2D.Core.Rendering;
using Xunit;

namespace ComputeShaders2D.Tests;

public class RendererIntegrationTests
{
    [Fact]
    public void ComputeSharpMatchesCpuHash()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var compute = new ComputeSharpVectorRenderer(128, 128);
        if (!compute.IsAvailable)
            return;

        var cpu = new CpuFallbackRenderer(128, 128);

        var api = new VectorApi(128, 128);
        var star = api.Path()
            .Poly(api.Star(64, 40, 30, 12, 5), close: true);
        api.FillPath(star, api.Color(200, 100, 255, 230));
        var zig = api.Path()
            .MoveTo(10, 100)
            .LineTo(118, 110)
            .LineTo(20, 118);
        api.StrokePath(zig, 6, api.Color(30, 200, 140, 255));

        var scene = api.BuildScene();

        var gpuBuffer = new byte[compute.RowPitch * compute.Height];
        compute.Render(scene, gpuBuffer);
        var gpuHash = compute.LastFrameHash;

        var cpuBuffer = new byte[cpu.RowPitch * cpu.Height];
        cpu.Render(scene, cpuBuffer);
        var cpuHash = RendererDiagnostics.ComputeHash(cpuBuffer);

        Assert.Equal(cpuHash, gpuHash);
    }
}
