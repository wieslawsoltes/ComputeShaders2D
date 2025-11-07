using System.Runtime.InteropServices;

namespace ComputeShaders2D.Core.Rendering;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ShapeGpu
{
    public uint VStart;
    public uint VCount;
    public FillRule Rule;
    public uint _pad0;
    public float ColorR;
    public float ColorG;
    public float ColorB;
    public float ColorA;
    public uint ClipStart;
    public uint ClipCount;
    public uint MaskStart;
    public uint MaskCount;
    public float Opacity;
    public float _pad1;
    public float _pad2;
    public float _pad3;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ClipGpu
{
    public uint VStart;
    public uint VCount;
    public FillRule Rule;
    public uint _pad0;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MaskGpu
{
    public uint VStart;
    public uint VCount;
    public FillRule Rule;
    public uint _pad0;
    public float Alpha;
    public float _pad1;
    public float _pad2;
    public float _pad3;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct UniformsGpu
{
    public uint CanvasW;
    public uint CanvasH;
    public uint TileSize;
    public uint TilesX;
    public uint Supersample;
    public uint _padA;
    public uint _padB;
    public uint _padC;
}
