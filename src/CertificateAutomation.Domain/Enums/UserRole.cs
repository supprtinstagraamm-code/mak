namespace CertificateAutomation.Domain.Enums;

/// <summary>نقش کاربر در سامانه.</summary>
public enum UserRole
{
    /// <summary>کاربر عادی: فقط صدور و چاپ نامه.</summary>
    Operator = 0,

    /// <summary>مدیر: دسترسی کامل شامل تنظیمات، حذف سابقه و تغییر شماره.</summary>
    Admin = 1
}
