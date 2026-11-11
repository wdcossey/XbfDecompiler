using System.Windows;
using System.Windows.Threading;

namespace XbfDecompiler;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // Allow "XbfDecompiler.exe file.xbf", which also makes "Open with" and drag-onto-exe work.
        new MainWindow(e.Args.FirstOrDefault()).Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(MainWindow, e.Exception.ToString(), "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
