using ComputeShaders2D.Core.Rendering;
using Xunit;

namespace ComputeShaders2D.Tests;

public class TileBinnerTests
{
    [Fact]
    public void CountsAndOffsetsMatchExpectedTiles()
    {
        var tileSize = 32;
        var shapes = new[]
        {
            new ShapeGpu
            {
                VStart = 0,
                VCount = 5,
                Rule = FillRule.EvenOdd,
                ClipStart = 0,
                ClipCount = 0,
                MaskStart = 0,
                MaskCount = 0,
                Opacity = 1f
            }
        };

        var vertices = new float[]
        {
            0, 0,
            16, 0,
            16, 16,
            0, 16,
            0, 0
        };

        var result = TileBinner.Build(shapes, vertices, 128, 128, tileSize);

        Assert.Equal(16, result.TileOffsetCounts.Length / 2);
        Assert.Equal((uint)0, result.TileOffsetCounts[0]);
        Assert.Equal((uint)1, result.TileOffsetCounts[1]);
        Assert.Equal(0u, result.TileShapeIndices[0]);

        for (var i = 1; i < result.TileOffsetCounts.Length / 2; i++)
        {
            Assert.Equal(0u, result.TileOffsetCounts[i * 2 + 1]);
        }
    }
}
