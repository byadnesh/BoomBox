using System.Windows;
using BoomBox.Core;

namespace BoomBox;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Explorer's right-click entry calls: BoomBox.exe open "<path>"
        if (e.Args is ["open", var path])
        {
            try
            {
                Launcher.OpenPath(path);
            }
            catch (BoomBoxException error)
            {
                MessageBox.Show(error.Message, "BoomBox", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }
}
