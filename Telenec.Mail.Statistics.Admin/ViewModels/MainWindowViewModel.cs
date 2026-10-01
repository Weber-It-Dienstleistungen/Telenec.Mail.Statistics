using CommunityToolkit.Mvvm.ComponentModel;

namespace Telenec.Mail.Statistics.Admin.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public string WindowTitle => "Telenec Mail Statistik";

    public string DashboardTitle => "Nutzungsstatistik";

    public string DashboardSubtitle =>
        "Übersicht der aktiven Telenec-Mail-Installationen";

    public string StatusText =>
        "Testdaten – noch keine Serververbindung";

    [ObservableProperty]
    private int totalInstallations = 128;

    [ObservableProperty]
    private int activeToday = 91;

    [ObservableProperty]
    private int activeLast7Days = 114;

    [ObservableProperty]
    private int activeLast30Days = 123;

    [ObservableProperty]
    private int inactiveOver30Days = 5;
}