using System.Windows.Controls;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CertificateAutomation.UI.Views;

/// <summary>نمای تنظیمات. دیالوگ ورود را به ViewModel تزریق می‌کند.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.ShowLoginDialog is null)
        {
            // تابع نمایش دیالوگ ورود را فراهم می‌کنیم (View مسئول پنجره است، نه ViewModel).
            vm.ShowLoginDialog = () =>
            {
                var session = App.Services?.GetService<IUserSession>();
                if (session is null) return false;
                var dialog = new LoginDialog(session) { Owner = System.Windows.Window.GetWindow(this) };
                return dialog.ShowDialog() == true;
            };
        }
    }
}
