using System;
using System.ComponentModel;
using ComputeShaders2D.AvaloniaApp.Models;
using ComputeShaders2D.Core.Rendering;

namespace ComputeShaders2D.AvaloniaApp.Rendering;

public sealed class SceneController : ISceneController, INotifyPropertyChanged, IDisposable
{
    private readonly SceneFactory _factory = new();
    private RendererMode _rendererMode = RendererMode.Auto;
    private SceneSampleKind _sample = SceneSampleKind.Default;
    private bool _isPlaying;
    private bool _randomizeRequest;
    private bool _resetTimeRequest;
    private string _scriptText = SampleScripts.DefaultSample;
    private bool _useScript;
    private IFont? _fontCache;
    private string? _fontKey;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SceneInvalidated;

    public RenderSettings Settings { get; } = new();
    public RenderStats Stats { get; } = new();

    public RendererMode RendererMode
    {
        get => _rendererMode;
        set
        {
            if (_rendererMode != value)
            {
                _rendererMode = value;
                OnPropertyChanged(nameof(RendererMode));
                SceneInvalidated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public SceneSampleKind Sample
    {
        get => _sample;
        set
        {
            if (_sample != value)
            {
                _sample = value;
                ScriptText = SampleScripts.GetSampleText(value);
                SceneInvalidated?.Invoke(this, EventArgs.Empty);
                OnPropertyChanged(nameof(Sample));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                OnPropertyChanged(nameof(IsPlaying));
            }
        }
    }

    public bool RandomizeRequested
    {
        get => _randomizeRequest;
        private set
        {
            if (_randomizeRequest != value)
            {
                _randomizeRequest = value;
                OnPropertyChanged(nameof(RandomizeRequested));
            }
        }
    }

    public string ScriptText
    {
        get => _scriptText;
        set
        {
            if (_scriptText != value)
            {
                _scriptText = value;
                OnPropertyChanged(nameof(ScriptText));
            }
        }
    }

    public bool UseScript => _useScript;

    public void SetCanvasSize(int width, int height)
    {
        if (Settings.CanvasWidth != width || Settings.CanvasHeight != height)
        {
            Settings.CanvasWidth = width;
            Settings.CanvasHeight = height;
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetTileSize(int tileSize)
    {
        if (Settings.TileSize != tileSize)
        {
            Settings.TileSize = tileSize;
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSupersample(int ss)
    {
        if (Settings.Supersample != ss)
        {
            Settings.Supersample = ss;
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetStrokeWidth(float value)
    {
        if (Math.Abs(Settings.StrokeWidth - value) > float.Epsilon)
        {
            Settings.StrokeWidth = value;
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetStrokeJoin(StrokeJoin join)
    {
        if (Settings.StrokeJoin != join)
        {
            Settings.StrokeJoin = join;
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetStrokeCap(StrokeCap cap)
    {
        if (Settings.StrokeCap != cap)
        {
            Settings.StrokeCap = cap;
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetMiterLimit(float value)
    {
        if (Math.Abs(Settings.MiterLimit - value) > float.Epsilon)
        {
            Settings.MiterLimit = value;
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ToggleFillRule()
    {
        Settings.FillRule = Settings.FillRule == FillRule.EvenOdd ? FillRule.NonZero : FillRule.EvenOdd;
        SceneInvalidated?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(Settings));
    }

    public void SetUseSystemFont(bool value)
    {
        if (Settings.UseSystemFont != value)
        {
            Settings.UseSystemFont = value;
            if (!value)
                DisposeFontCache();
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetFontFamily(string? family)
    {
        family ??= string.Empty;
        if (Settings.FontFamily != family)
        {
            Settings.FontFamily = family;
            DisposeFontCache();
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetSvgOverlay(bool value)
    {
        if (Settings.ShowSvgOverlay != value)
        {
            Settings.ShowSvgOverlay = value;
            SceneInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    public void RequestRandomizeLines()
    {
        RandomizeRequested = true;
        SceneInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public void InvalidateScene() => SceneInvalidated?.Invoke(this, EventArgs.Empty);

    public PackedScene BuildScene(RenderFrameContext context)
    {
        var font = GetActiveFont();

        if (_useScript && !string.IsNullOrWhiteSpace(_scriptText))
        {
            if (SceneScriptRunner.TryBuild(_scriptText, context, Settings, font, out var scriptScene, out _))
            {
                RandomizeRequested = false;
                _resetTimeRequest = false;
                return scriptScene!;
            }
        }

        var randomize = RandomizeRequested;
        RandomizeRequested = false;
        _resetTimeRequest = false;
        return _factory.BuildScene(context, Settings, _sample, randomize, font);
    }

    public void UpdateStats(double buildMs, double rasterMs, double frameMs, double fps, string tilesText, string modeText)
    {
        Stats.BuildMs = buildMs;
        Stats.RasterMs = rasterMs;
        Stats.FrameMs = frameMs;
        Stats.Fps = fps;
        Stats.TilesText = tilesText;
        Stats.ModeText = modeText;
        OnPropertyChanged(nameof(Stats));
    }

    public bool ResetTimeRequested => _resetTimeRequest;

    public void RequestResetTime()
    {
        _resetTimeRequest = true;
        SceneInvalidated?.Invoke(this, EventArgs.Empty);
    }

    public void ClearResetRequest() => _resetTimeRequest = false;

    public void EnableScript(string source)
    {
        ScriptText = source;
        _useScript = true;
        SceneInvalidated?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(UseScript));
    }

    public void DisableScript()
    {
        _useScript = false;
        SceneInvalidated?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(UseScript));
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private IFont? GetActiveFont()
    {
        if (!Settings.UseSystemFont)
            return null;

        var key = (Settings.FontFamily ?? string.Empty).Trim();
        if (_fontCache != null && string.Equals(_fontKey, key, StringComparison.OrdinalIgnoreCase))
            return _fontCache;

        DisposeFontCache();
        try
        {
            _fontCache = FontLoader.LoadAsync(key).GetAwaiter().GetResult();
            _fontKey = key;
        }
        catch
        {
            _fontCache = null;
            _fontKey = null;
        }
        return _fontCache;
    }

    private void DisposeFontCache()
    {
        if (_fontCache is IDisposable disposable)
            disposable.Dispose();
        _fontCache = null;
        _fontKey = null;
    }

    public void Dispose() => DisposeFontCache();
}
