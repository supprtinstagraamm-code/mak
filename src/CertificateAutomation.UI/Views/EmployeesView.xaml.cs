using System.Windows.Controls;
using CertificateAutomation.UI.ViewModels;

namespace CertificateAutomation.UI.Views;

/// <summary>نمای فهرست کارکنان.</summary>
public partial class EmployeesView : UserControl
{
    public EmployeesView() => InitializeComponent();

    /// <summary>پاک کردن متن جستجو. منطق ساده UI است و در ViewModel جایی ندارد.</summary>
    private void ClearSearch(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is EmployeesViewModel vm)
            vm.SearchText = string.Empty;
    }
}
