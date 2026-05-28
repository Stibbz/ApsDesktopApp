using System.Windows;

namespace ApsDesktopApp.Views;

public partial class ConvertFileWindow : Window
{
    public ConvertFileWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
