using ComputeShaders2D.Core.Rendering;
using Xunit;

namespace ComputeShaders2D.Tests;

public class UnitTest1
{
    [Fact]
    public void PackedSceneContainsFilledShape()
    {
        var api = new VectorApi(256, 256);
        api.FillPath(api.Path()
            .MoveTo(10, 10)
            .LineTo(200, 10)
            .LineTo(200, 200)
            .LineTo(10, 200)
            .ClosePath(), api.Color(255, 0, 0, 255));

        var scene = api.BuildScene();

        Assert.NotEmpty(scene.Shapes);
        Assert.NotEmpty(scene.Vertices);
        Assert.NotEmpty(scene.TileOffsetCounts);
        Assert.Equal((uint)256, scene.Uniforms.CanvasW);
    }
}
