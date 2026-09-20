using CertificateAutomation.Domain.Enums;

namespace CertificateAutomation.Domain.Entities;

/// <summary>کاربر سامانه. رمز عبور هرگز به‌صورت خام ذخیره نمی‌شود.</summary>
public class AppUser
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>خروجی PBKDF2-SHA256 به‌صورت Base64.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>نمک تصادفی به‌صورت Base64.</summary>
    public string Salt { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Operator;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
