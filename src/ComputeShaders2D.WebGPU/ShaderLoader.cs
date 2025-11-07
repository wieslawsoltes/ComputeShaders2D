using System;
using System.IO;
using System.Reflection;

namespace ComputeShaders2D.WebGPU;

internal static class ShaderLoader
{
    public static string Load(string resourcePath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var fullName = resourcePath;
        if (!resourcePath.StartsWith(assembly.GetName().Name!, StringComparison.Ordinal))
        {
            fullName = $"{assembly.GetName().Name}.{resourcePath}";
        }

        using var stream = assembly.GetManifestResourceStream(fullName)
            ?? throw new InvalidOperationException($"Could not locate shader resource '{fullName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
