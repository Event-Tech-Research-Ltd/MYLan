using Avalonia.Controls;
using DhcpFieldServer.ViewModels;

namespace DhcpFieldServer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    // Closing previously skipped Stop(): the adapter stayed on the static IP and
    // the last 2 s of lease changes were never saved.
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as MainWindowViewModel)?.Shutdown();
        base.OnClosed(e);
    }
}
