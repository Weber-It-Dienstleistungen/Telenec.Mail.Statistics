using CommunityToolkit.Mvvm.ComponentModel;

namespace Telenec.Mail.Statistics.Admin.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public string WindowTitle => "Telenec Mail Statistik";

    public string StatusText => "Admin-Oberfläche bereit";
}