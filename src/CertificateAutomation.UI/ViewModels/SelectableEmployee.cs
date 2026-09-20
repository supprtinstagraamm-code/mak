using CertificateAutomation.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CertificateAutomation.UI.ViewModels;

/// <summary>
/// پوششی روی Employee برای حالت انتخاب چندگانه (چاپ گروهی).
/// Employee خودش تغییر نمی‌کند؛ فقط یک تیک انتخاب کنارش اضافه می‌شود.
/// </summary>
public partial class SelectableEmployee : ObservableObject
{
    public Employee Employee { get; }

    [ObservableProperty] private bool _isSelected;

    public SelectableEmployee(Employee employee) => Employee = employee;

    public string FullName => Employee.FullName;
    public string? NationalId => Employee.NationalId;
    public string? Position => Employee.Position;
    public string? Unit => Employee.Unit;
    public long Id => Employee.Id;
}
