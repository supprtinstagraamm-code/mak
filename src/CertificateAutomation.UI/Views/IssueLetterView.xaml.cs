using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using CertificateAutomation.UI.ViewModels;

namespace CertificateAutomation.UI.Views;

/// <summary>نمای ویزارد صدور نامه.</summary>
public partial class IssueLetterView : UserControl
{
    public IssueLetterView() => InitializeComponent();

    /// <summary>
    /// دابل‌کلیک روی یک شخص → رفتن مستقیم به مرحلهٔ بعد (انتخاب قالب).
    /// فقط وقتی روی یک ردیف واقعی کلیک شده باشد عمل می‌کند، نه روی سربرگ یا فضای خالی.
    /// </summary>
    private void EmployeeGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => AdvanceIfRowClicked(e);

    /// <summary>دابل‌کلیک روی یک قالب → رفتن مستقیم به مرحلهٔ پیش‌نمایش.</summary>
    private void TemplateGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => AdvanceIfRowClicked(e);

    /// <summary>
    /// اگر دابل‌کلیک روی یک ردیف داده بوده، دستور «مرحلهٔ بعد» اجرا می‌شود.
    /// انتخاب ردیف پیش از این رویداد توسط خود DataGrid انجام شده است.
    /// </summary>
    private void AdvanceIfRowClicked(MouseButtonEventArgs e)
    {
        if (!IsClickOnDataRow(e.OriginalSource as DependencyObject)) return;

        if (DataContext is IssueLetterViewModel vm && vm.NextCommand.CanExecute(null))
            vm.NextCommand.Execute(null);
    }

    /// <summary>پیمایش درخت بصری برای تشخیص اینکه منشأ کلیک درون یک DataGridRow است.</summary>
    private static bool IsClickOnDataRow(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is DataGridRow) return true;
            if (source is DataGridColumnHeader) return false;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}
