using System.Windows;

namespace SmartX.WPF;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenIngestion_Click(object sender, RoutedEventArgs e)
    {
        var ingestionWindow = new IngestionWindow();
        ingestionWindow.Show();
        this.Close();
    }
}