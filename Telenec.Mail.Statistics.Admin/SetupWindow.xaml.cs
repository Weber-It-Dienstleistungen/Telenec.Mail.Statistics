using System.Windows;
using Telenec.Mail.Statistics.Admin.ViewModels;

namespace Telenec.Mail.Statistics.Admin;

public partial class SetupWindow : Window
{
    private readonly SetupWindowViewModel _viewModel;

    public SetupWindow(
        SetupWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void OnSetupClick(
        object sender,
        RoutedEventArgs e)
    {
        var successful =
            await _viewModel.ConfigureAsync(
                AdminApiKeyPasswordBox.Password);

        if (!successful)
        {
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult = false;
    }
}