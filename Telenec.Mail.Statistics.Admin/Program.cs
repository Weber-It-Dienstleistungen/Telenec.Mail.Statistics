using Velopack;

namespace Telenec.Mail.Statistics.Admin;

internal static class Program
{
    [STAThread]
    private static void Main(
        string[] args)
    {
        VelopackApp
            .Build()
            .Run();

        var application =
            new App();

        application.Run();
    }
}