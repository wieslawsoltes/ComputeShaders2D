using Avalonia.Controls;

using ComputeShaders2D.AvaloniaApp.ViewModels;

namespace ComputeShaders2D.AvaloniaApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }
}
