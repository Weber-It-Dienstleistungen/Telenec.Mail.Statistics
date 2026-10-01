using System.Windows;
using Telenec.Mail.Statistics.Admin.ViewModels;

namespace Telenec.Mail.Statistics.Admin;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}