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
}
