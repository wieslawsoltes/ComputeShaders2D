using System.Linq;
using ComputeShaders2D.Core.Rendering;
using Xunit;

namespace ComputeShaders2D.Tests;

public class VectorApiTests
{
    [Fact]
    public void SvgPath_WithArcCommand_FlattensToVertices()
    {
        var api = new VectorApi(200, 200);
        var path = api.SvgPath("M 10 10 A 40 40 0 0 1 90 10");
        var contours = path.Flatten();
        Assert.NotEmpty(contours);
        Assert.Contains(contours, poly => poly.Length > 3);
    }

    [Fact]
    public void FillText_ProducesShapes()
    {
        var api = new VectorApi(256, 256);
        var font = FontLoader.GetDefault();
        api.FillText(font, "Hi", 40, 80, 32, api.Color(255, 255, 0, 255));
        var scene = api.BuildScene();
        Assert.NotEmpty(scene.Shapes);
    }

    [Fact]
    public void StrokePath_WithJoinAndCap_GeneratesMultiplePolygons()
    {
        var api = new VectorApi(256, 256);
        var path = api.Path()
            .MoveTo(20, 20)
            .LineTo(120, 20)
            .LineTo(120, 120);

        api.StrokePath(path, 12, api.Color(0, 255, 0, 255), new StrokeStyle(StrokeJoin.Round, StrokeCap.Round, 4f));
        var scene = api.BuildScene();
        Assert.True(scene.Shapes.Length >= 3);
    }
}
