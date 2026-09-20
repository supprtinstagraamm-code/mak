using System.Windows;
using System.Windows.Input;
using CertificateAutomation.Application.Abstractions;

namespace CertificateAutomation.UI.Views;

/// <summary>دیالوگ ورود مدیر. در صورت موفقیت DialogResult=true برمی‌گرداند.</summary>
public partial class LoginDialog : Window
{
    private readonly IUserSession _session;

    public LoginDialog(IUserSession session)
    {
        InitializeComponent();
        _session = session;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private async void Login_Click(object sender, RoutedEventArgs e) => await TryLoginAsync();

    private async void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await TryLoginAsync();
    }

    private async System.Threading.Tasks.Task TryLoginAsync()
    {
        ErrorText.Visibility = Visibility.Collapsed;

        var result = await _session.LoginAsync(UsernameBox.Text, PasswordBox.Password);
        if (result.IsSuccess)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            ErrorText.Text = result.Error;
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
