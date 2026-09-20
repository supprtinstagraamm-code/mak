namespace CertificateAutomation.Domain.Entities;

/// <summary>
/// یک کارمند/عضو که اطلاعاتش از فایل Excel خوانده و در پایگاه داده کش شده است.
/// ستون‌هایی که در ساختار ثابت زیر نیستند، در <see cref="ExtraData"/> نگهداری می‌شوند
/// تا افزودن ستون جدید به Excel نیازی به تغییر پایگاه داده نداشته باشد.
/// </summary>
public class Employee
{
    public long Id { get; set; }
    public string? PersonnelCode { get; set; }
    public string? MembershipNo { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? FatherName { get; set; }
    public string? NationalId { get; set; }
    public string? Position { get; set; }
    public string? Unit { get; set; }
    public string? Phone { get; set; }

    /// <summary>تاریخ استخدام به‌صورت شمسی، مثلاً 1398/04/15.</summary>
    public string? HireDate { get; set; }

    /// <summary>ستون‌های اضافی Excel به‌صورت JSON.</summary>
    public string? ExtraDataJson { get; set; }

    /// <summary>هش سطر برای تشخیص تغییرات در بارگذاری بعدی.</summary>
    public string? RowHash { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime ImportedAt { get; set; }

    /// <summary>نام کامل برای نمایش در فهرست‌ها.</summary>
    public string FullName => $"{FirstName} {LastName}".Trim();
}
