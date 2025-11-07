using System;

namespace ComputeShaders2D.Core.Rendering;

public static class RendererDiagnostics
{
    public static ulong ComputeHash(ReadOnlySpan<byte> data)
    {
        const ulong offset = 1469598103934665603;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var b in data)
        {
            hash ^= b;
            hash *= prime;
        }
        return hash;
    }
}
