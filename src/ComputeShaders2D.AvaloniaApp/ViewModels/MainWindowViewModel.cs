using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ComputeShaders2D.AvaloniaApp.Models;
using ComputeShaders2D.AvaloniaApp.Rendering;
using ComputeShaders2D.AvaloniaApp.Utils;
using ComputeShaders2D.Core.Rendering;

namespace ComputeShaders2D.AvaloniaApp.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly SceneController _controller = new();
    private CanvasSizeOption _selectedCanvas;
    private RendererMode _rendererMode = RendererMode.Auto;

    public MainWindowViewModel()
    {
        CanvasSizes = new ObservableCollection<CanvasSizeOption>
        {
            new("800 × 600", 800, 600),
            new("1024 × 768", 1024, 768),
            new("1280 × 720", 1280, 720),
            new("1920 × 1080", 1920, 1080)
        };
        _selectedCanvas = CanvasSizes[0];

        _controller.PropertyChanged += OnControllerPropertyChanged;

        RenderCommand = new DelegateCommand(() => _controller.InvalidateScene());
        RandomizeCommand = new DelegateCommand(() => _controller.RequestRandomizeLines());
        ToggleFillRuleCommand = new DelegateCommand(() =>
        {
            _controller.ToggleFillRule();
            OnPropertyChanged(nameof(FillRuleText));
            OnPropertyChanged(nameof(FillRuleLabel));
        });
        PlayPauseCommand = new DelegateCommand(() =>
        {
            _controller.IsPlaying = !_controller.IsPlaying;
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(PlayPauseLabel));
        });
        ResetTimeCommand = new DelegateCommand(() => { _controller.RequestResetTime(); _controller.InvalidateScene(); });
        LoadDefaultCommand = new DelegateCommand(() => { _controller.Sample = SceneSampleKind.Default; NotifySampleChanged(); });
        LoadAnimationCommand = new DelegateCommand(() => { _controller.Sample = SceneSampleKind.Animation; NotifySampleChanged(); });
        LoadClipCommand = new DelegateCommand(() => { _controller.Sample = SceneSampleKind.Clip; NotifySampleChanged(); });
        LoadMaskCommand = new DelegateCommand(() => { _controller.Sample = SceneSampleKind.Mask; NotifySampleChanged(); });
        RunScriptCommand = new DelegateCommand(() =>
        {
            _controller.EnableScript(ScriptText);
            OnPropertyChanged(nameof(IsScriptActive));
            OnPropertyChanged(nameof(ScriptStatus));
        });
        StopScriptCommand = new DelegateCommand(() =>
        {
            _controller.DisableScript();
            OnPropertyChanged(nameof(IsScriptActive));
            OnPropertyChanged(nameof(ScriptStatus));
        });

        ApplyCanvasSelection(_selectedCanvas);
        TileSize = _controller.Settings.TileSize;
        Supersample = _controller.Settings.Supersample;
        StrokeWidth = _controller.Settings.StrokeWidth;
        MiterLimit = _controller.Settings.MiterLimit;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ISceneController SceneController => _controller;

    public ObservableCollection<CanvasSizeOption> CanvasSizes { get; }
    public Array RendererModes { get; } = Enum.GetValues(typeof(RendererMode));
    public Array StrokeJoinValues { get; } = Enum.GetValues(typeof(StrokeJoin));
    public Array StrokeCapValues { get; } = Enum.GetValues(typeof(StrokeCap));
    public int[] SupersampleOptions { get; } = new[] { 1, 2, 4 };

    public CanvasSizeOption SelectedCanvas
    {
        get => _selectedCanvas;
        set
        {
            if (_selectedCanvas != value)
            {
                _selectedCanvas = value;
                ApplyCanvasSelection(value);
                OnPropertyChanged();
            }
        }
    }

    public RendererMode RendererMode
    {
        get => _rendererMode;
        set
        {
            if (_rendererMode != value)
            {
                _rendererMode = value;
                _controller.RendererMode = value;
                OnPropertyChanged();
            }
        }
    }

    public int TileSize
    {
        get => _controller.Settings.TileSize;
        set
        {
            if (_controller.Settings.TileSize != value)
            {
                _controller.SetTileSize(value);
                OnPropertyChanged();
            }
        }
    }

    public int Supersample
    {
        get => _controller.Settings.Supersample;
        set
        {
            if (_controller.Settings.Supersample != value)
            {
                _controller.SetSupersample(value);
                OnPropertyChanged();
            }
        }
    }

    public float StrokeWidth
    {
        get => _controller.Settings.StrokeWidth;
        set
        {
            if (Math.Abs(_controller.Settings.StrokeWidth - value) > float.Epsilon)
            {
                _controller.SetStrokeWidth(value);
                OnPropertyChanged();
            }
        }
    }

    public StrokeJoin StrokeJoin
    {
        get => _controller.Settings.StrokeJoin;
        set
        {
            if (_controller.Settings.StrokeJoin != value)
            {
                _controller.SetStrokeJoin(value);
                OnPropertyChanged();
            }
        }
    }

    public StrokeCap StrokeCap
    {
        get => _controller.Settings.StrokeCap;
        set
        {
            if (_controller.Settings.StrokeCap != value)
            {
                _controller.SetStrokeCap(value);
                OnPropertyChanged();
            }
        }
    }

    public float MiterLimit
    {
        get => _controller.Settings.MiterLimit;
        set
        {
            if (Math.Abs(_controller.Settings.MiterLimit - value) > float.Epsilon)
            {
                _controller.SetMiterLimit(value);
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlaying => _controller.IsPlaying;

    public string FillRuleText => _controller.Settings.FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero";
    public string FillRuleLabel => $"Fill Rule: {FillRuleText}";

    public RenderStats Stats => _controller.Stats;
    public string ScriptText
    {
        get => _controller.ScriptText;
        set
        {
            if (_controller.ScriptText != value)
            {
                _controller.ScriptText = value;
                OnPropertyChanged();
            }
        }
    }
    public string ModeText => _controller.IsPlaying ? "Animating" : "Static";
    public string PlayPauseLabel => _controller.IsPlaying ? "Pause" : "Play";
    public bool IsScriptActive => _controller.UseScript;
    public string ScriptStatus => IsScriptActive ? "Script mode active" : "Using built-in samples";
    public bool UseSystemFont
    {
        get => _controller.Settings.UseSystemFont;
        set
        {
            if (_controller.Settings.UseSystemFont != value)
            {
                _controller.SetUseSystemFont(value);
                OnPropertyChanged();
            }
        }
    }

    public string FontFamily
    {
        get => _controller.Settings.FontFamily;
        set
        {
            var normalized = value ?? string.Empty;
            if (_controller.Settings.FontFamily != normalized)
            {
                _controller.SetFontFamily(normalized);
                OnPropertyChanged();
            }
        }
    }

    public bool ShowSvgOverlay
    {
        get => _controller.Settings.ShowSvgOverlay;
        set
        {
            if (_controller.Settings.ShowSvgOverlay != value)
            {
                _controller.SetSvgOverlay(value);
                OnPropertyChanged();
            }
        }
    }

    public ICommand RenderCommand { get; }
    public ICommand RandomizeCommand { get; }
    public ICommand ToggleFillRuleCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand ResetTimeCommand { get; }
    public ICommand LoadDefaultCommand { get; }
    public ICommand LoadAnimationCommand { get; }
    public ICommand LoadClipCommand { get; }
    public ICommand LoadMaskCommand { get; }
    public ICommand RunScriptCommand { get; }
    public ICommand StopScriptCommand { get; }

    private void ApplyCanvasSelection(CanvasSizeOption option)
    {
        _controller.SetCanvasSize(option.Width, option.Height);
        OnPropertyChanged(nameof(SceneController));
    }

    private void NotifySampleChanged()
    {
        _controller.DisableScript();
        OnPropertyChanged(nameof(ScriptText));
        OnPropertyChanged(nameof(IsScriptActive));
        OnPropertyChanged(nameof(ScriptStatus));
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SceneController.Stats))
        {
            OnPropertyChanged(nameof(Stats));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
