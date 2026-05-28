using System.Windows;
using System.Windows.Controls;

namespace ApsDesktopApp.Views;

public partial class FileConverterView : UserControl
{
    private LogViewerWindow? _logWindow;

    public FileConverterView()
    {
        InitializeComponent();
    }

    private void ShowLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow is null)
        {
            _logWindow = new LogViewerWindow
            {
                Owner = Window.GetWindow(this)
            };
            // Allow the window to be reopened after the user closes it.
            _logWindow.Closed += (_, _) => _logWindow = null;
        }
        _logWindow.Show();
        _logWindow.Activate();
    }
}
