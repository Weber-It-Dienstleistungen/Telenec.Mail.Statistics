using System.Windows;
using Telenec.Mail.Statistics.Admin.ViewModels;

namespace Telenec.Mail.Statistics.Admin;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(
        MainWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        await _viewModel.LoadAsync();
    }
}