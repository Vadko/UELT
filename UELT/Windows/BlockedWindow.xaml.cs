using System.Windows;
using System.Windows.Input;

namespace UELT;

public partial class BlockedWindow : Window
{
    public BlockedWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}