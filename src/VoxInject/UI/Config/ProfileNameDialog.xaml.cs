using System.Windows;
using System.Windows.Input;

namespace VoxInject.UI.Config;

public partial class ProfileNameDialog : Window
{
    public string ProfileName => NameBox.Text.Trim();

    public ProfileNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }
}
