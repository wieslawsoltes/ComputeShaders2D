using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ComputeShaders2D.Core.Rendering;
using ComputeShaders2D.WebGPU;
using ComputeShaders2D.AvaloniaApp.Rendering;
using ComputeShaders2D.AvaloniaApp.Models;
using System.Diagnostics;
using ComputeShaders2D.ComputeSharp;

namespace ComputeShaders2D.AvaloniaApp.Controls;

public sealed class GpuCanvas : Control, IDisposable
{
    public static readonly StyledProperty<ISceneController?> SceneControllerProperty =
        AvaloniaProperty.Register<GpuCanvas, ISceneController?>(nameof(SceneController));

    private WriteableBitmap? _bitmap;
    private byte[] _scratch = Array.Empty<byte>();
    private WgpuVectorRenderer? _gpuRenderer;
    private ComputeSharpVectorRenderer? _computeRenderer;
    private CpuFallbackRenderer? _cpuRenderer;
    private readonly DispatcherTimer _timer;
    private readonly EventHandler _tickHandler;
    private double _timeSeconds;
    private ulong _frameIndex;
    private long _lastTickMs;
    private ISceneController? _currentController;

    public GpuCanvas()
    {
        _tickHandler = (_, _) => RenderFrame();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += _tickHandler;
        _timer.Start();

    }

    public ISceneController? SceneController
    {
        get => GetValue(SceneControllerProperty);
        set => SetValue(SceneControllerProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneControllerProperty)
        {
            OnSceneControllerChanged(change.GetNewValue<ISceneController?>());
        }
    }

    private void OnSceneControllerChanged(ISceneController? controller)
    {
        if (_currentController != null)
        {
            _currentController.SceneInvalidated -= OnSceneInvalidated;
        }

        _currentController = controller;
        if (_currentController != null)
        {
            _currentController.SceneInvalidated += OnSceneInvalidated;
        }
        RenderFrame();
    }

    private void OnSceneInvalidated(object? sender, EventArgs e) => RenderFrame();

    private void RenderFrame()
    {
        var controller = SceneController;
        if (controller == null)
            return;

        var width = (uint)controller.Settings.CanvasWidth;
        var height = (uint)controller.Settings.CanvasHeight;
        if (width == 0 || height == 0)
            return;

        EnsureBitmap(width, height);

        if (controller.ResetTimeRequested)
        {
            _timeSeconds = 0;
            _frameIndex = 0;
            controller.ClearResetRequest();
        }

        var nowMs = Environment.TickCount64;
        var dt = _lastTickMs == 0 ? 0 : (nowMs - _lastTickMs) / 1000.0;
        _lastTickMs = nowMs;
        if (controller.IsPlaying)
        {
            _timeSeconds += dt;
        }

        var context = new RenderFrameContext(width, height, _timeSeconds, dt, _frameIndex++);
        var stopwatch = Stopwatch.StartNew();
        var scene = controller.BuildScene(context);
        var buildMs = stopwatch.Elapsed.TotalMilliseconds;

        var renderer = SelectRenderer(controller.RendererMode, width, height);
        if (renderer == null)
            return;

        EnsureScratch(renderer.RowPitch, renderer.Height);
        stopwatch.Restart();
        renderer.Render(scene, _scratch);
        var rasterMs = stopwatch.Elapsed.TotalMilliseconds;

        CopyToBitmap(renderer.RowPitch, width, height);
        InvalidateVisual();

        var frameMs = buildMs + rasterMs;
        var fps = frameMs > 0 ? 1000.0 / frameMs : 0;
        var tilesY = (int)Math.Ceiling((double)scene.Uniforms.CanvasH / scene.Uniforms.TileSize);
        var tilesText = $"{scene.Uniforms.TilesX} × {tilesY} = {scene.Uniforms.TilesX * tilesY}";
        controller.UpdateStats(buildMs, rasterMs, frameMs, fps, tilesText, controller.IsPlaying ? "Animating" : "Static");
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_bitmap != null)
        {
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), new Rect(Bounds.Size));
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= _tickHandler;
        _gpuRenderer?.Dispose();
        _computeRenderer?.Dispose();
        _cpuRenderer?.Dispose();
        _bitmap?.Dispose();
    }

    private void EnsureBitmap(uint width, uint height)
    {
        if (_bitmap != null && _bitmap.PixelSize.Width == width && _bitmap.PixelSize.Height == height)
            return;

        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
    }

    private void EnsureScratch(uint rowPitch, uint height)
    {
        var required = rowPitch * height;
        if (_scratch.Length < required)
        {
            Array.Resize(ref _scratch, (int)required);
        }
    }

    private IVectorRenderer? SelectRenderer(RendererMode mode, uint width, uint height)
    {
        var gpu = EnsureGpuRenderer(width, height);
        var computeSharp = EnsureComputeSharpRenderer(width, height);
        var cpu = EnsureCpuRenderer(width, height);

        return mode switch
        {
            RendererMode.Cpu => cpu,
            RendererMode.WebGpu => gpu ?? computeSharp ?? cpu,
            RendererMode.ComputeSharp => computeSharp ?? cpu,
            _ => gpu ?? computeSharp ?? cpu
        };
    }

    private IVectorRenderer? EnsureGpuRenderer(uint width, uint height)
    {
        if (_gpuRenderer != null && _gpuRenderer.Width == width && _gpuRenderer.Height == height)
            return _gpuRenderer.IsAvailable ? _gpuRenderer : null;

        _gpuRenderer?.Dispose();
        try
        {
            var candidate = new WgpuVectorRenderer(width, height);
            if (candidate.IsAvailable)
            {
                _gpuRenderer = candidate;
                return _gpuRenderer;
            }
            candidate.Dispose();
        }
        catch
        {
            _gpuRenderer = null;
        }
        return _gpuRenderer;
    }

    private IVectorRenderer? EnsureComputeSharpRenderer(uint width, uint height)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        if (_computeRenderer != null && _computeRenderer.Width == width && _computeRenderer.Height == height)
            return _computeRenderer.IsAvailable ? _computeRenderer : null;

        _computeRenderer?.Dispose();
        try
        {
            var renderer = new ComputeSharpVectorRenderer(width, height);
            if (renderer.IsAvailable)
            {
                _computeRenderer = renderer;
                return _computeRenderer;
            }
            renderer.Dispose();
        }
        catch
        {
            _computeRenderer = null;
        }

        return _computeRenderer;
    }

    private IVectorRenderer EnsureCpuRenderer(uint width, uint height)
    {
        if (_cpuRenderer != null && _cpuRenderer.Width == width && _cpuRenderer.Height == height)
            return _cpuRenderer;

        _cpuRenderer?.Dispose();
        _cpuRenderer = new CpuFallbackRenderer(width, height);
        return _cpuRenderer;
    }

    private void CopyToBitmap(uint rowPitch, uint width, uint height)
    {
        if (_bitmap == null)
            return;

        using var fb = _bitmap.Lock();
        var rowBytes = (int)(width * 4);
        unsafe
        {
            var pixelSize = _bitmap!.PixelSize;
            var dst = new Span<byte>(fb.Address.ToPointer(), fb.RowBytes * pixelSize.Height);
            for (var y = 0; y < height; y++)
            {
                var srcRow = new Span<byte>(_scratch, (int)(y * rowPitch), (int)rowPitch);
                var dstRow = dst.Slice(y * fb.RowBytes, fb.RowBytes);
                srcRow[..Math.Min(rowBytes, Math.Min(srcRow.Length, dstRow.Length))].CopyTo(dstRow);
            }
        }
    }
}
