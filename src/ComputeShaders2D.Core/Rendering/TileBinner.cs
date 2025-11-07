using System.Buffers;
using System.Numerics;

namespace ComputeShaders2D.Core.Rendering;

internal static class TileBinner
{
    internal sealed record TileResult(uint[] TileOffsetCounts, uint[] TileShapeIndices, int TilesX, int TilesY);

    public static TileResult Build(ShapeGpu[] shapes, float[] vertices, int width, int height, int tileSize)
    {
        var tilesX = (int)Math.Ceiling(width / (float)tileSize);
        var tilesY = (int)Math.Ceiling(height / (float)tileSize);
        var tileCount = tilesX * tilesY;
        if (tileCount == 0)
        {
            return new TileResult(Array.Empty<uint>(), Array.Empty<uint>(), tilesX, tilesY);
        }

        var pool = ArrayPool<uint>.Shared;
        var countsArray = pool.Rent(tileCount);
        var offsetsArray = Array.Empty<uint>();
        var cursorsArray = Array.Empty<uint>();
        try
        {
            var counts = countsArray.AsSpan(0, tileCount);
            counts.Clear();

            var ranges = new (int minTx, int maxTx, int minTy, int maxTy)[shapes.Length];

            for (var i = 0; i < shapes.Length; i++)
            {
                var shape = shapes[i];
                if (shape.VCount == 0)
                {
                    ranges[i] = (0, -1, 0, -1);
                    continue;
                }

                var bounds = ComputeBounds(shape, vertices);
                if (bounds is null)
                {
                    ranges[i] = (0, -1, 0, -1);
                    continue;
                }

                var (minX, minY, maxX, maxY) = bounds.Value;
                var minTx = ClampTile((int)MathF.Floor(minX / tileSize), tilesX);
                var maxTx = ClampTile((int)MathF.Floor(maxX / tileSize), tilesX);
                var minTy = ClampTile((int)MathF.Floor(minY / tileSize), tilesY);
                var maxTy = ClampTile((int)MathF.Floor(maxY / tileSize), tilesY);
                ranges[i] = (minTx, maxTx, minTy, maxTy);

                if (minTx > maxTx || minTy > maxTy)
                    continue;

                for (var ty = minTy; ty <= maxTy; ty++)
                for (var tx = minTx; tx <= maxTx; tx++)
                {
                    var tileIndex = ty * tilesX + tx;
                    counts[tileIndex]++;
                }
            }

            offsetsArray = pool.Rent(tileCount);
            var offsets = offsetsArray.AsSpan(0, tileCount);
            offsets.Clear();

            uint running = 0;
            for (var i = 0; i < tileCount; i++)
            {
                offsets[i] = running;
                running += counts[i];
            }

            var tileShapeIndices = new uint[running];

            cursorsArray = pool.Rent(tileCount);
            var cursors = cursorsArray.AsSpan(0, tileCount);
            offsets.CopyTo(cursors);

            for (var shapeIndex = 0; shapeIndex < shapes.Length; shapeIndex++)
            {
                var range = ranges[shapeIndex];
                if (range.minTx > range.maxTx || range.minTy > range.maxTy)
                    continue;

                for (var ty = range.minTy; ty <= range.maxTy; ty++)
                for (var tx = range.minTx; tx <= range.maxTx; tx++)
                {
                    var tileIndex = ty * tilesX + tx;
                    var cursor = cursors[tileIndex];
                    tileShapeIndices[cursor] = (uint)shapeIndex;
                    cursors[tileIndex] = cursor + 1;
                }
            }

            var tileOffsetCounts = new uint[tileCount * 2];
            for (var i = 0; i < tileCount; i++)
            {
                tileOffsetCounts[i * 2] = offsets[i];
                tileOffsetCounts[i * 2 + 1] = counts[i];
            }

            return new TileResult(tileOffsetCounts, tileShapeIndices, tilesX, tilesY);
        }
        finally
        {
            if (cursorsArray.Length > 0)
                pool.Return(cursorsArray, clearArray: true);
            if (offsetsArray.Length > 0)
                pool.Return(offsetsArray, clearArray: true);
            pool.Return(countsArray, clearArray: true);
        }
    }

    private static (float minX, float minY, float maxX, float maxY)? ComputeBounds(ShapeGpu shape, float[] vertices)
    {
        if (shape.VCount == 0)
            return null;

        var start = shape.VStart * 2;
        var end = start + shape.VCount * 2;
        if (end > vertices.Length)
            return null;

        var minX = float.PositiveInfinity;
        var minY = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var maxY = float.NegativeInfinity;

        for (var i = start; i < end; i += 2)
        {
            var x = vertices[i];
            var y = vertices[i + 1];
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }

        if (float.IsInfinity(minX))
            return null;

        return (minX, minY, maxX, maxY);
    }

    private static int ClampTile(int tileIndex, int tiles) => Math.Clamp(tileIndex, 0, Math.Max(tiles - 1, 0));
}
