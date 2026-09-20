using CertificateAutomation.Domain.Entities;

namespace CertificateAutomation.Application.Dtos;

/// <summary>آمار نمایش‌داده‌شده در داشبورد.</summary>
public class DashboardStats
{
    public int TodayCount { get; set; }
    public int MonthCount { get; set; }
    public int YearCount { get; set; }
    public int EmployeeCount { get; set; }
    public string? LastLetterNumber { get; set; }
    public List<Letter> RecentLetters { get; } = new();
}
