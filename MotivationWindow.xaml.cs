using System.Windows;

namespace ProductivityApp;

public partial class MotivationWindow : Window
{
    public MotivationWindow(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
