using System.Windows.Forms;
using BaudioApp.Views;

namespace BaudioApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single-instance guard — extra launches exit immediately.
        using var mutex = new Mutex(true, "BlueAmp_SingleInstance", out bool createdNew);
        if (!createdNew) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var ctx = new TrayApplicationContext();
        Application.Run(ctx);

        GC.KeepAlive(mutex);
    }
}
