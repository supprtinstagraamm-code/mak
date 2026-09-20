using CertificateAutomation.Domain.Entities;

namespace CertificateAutomation.Application.Dtos;

/// <summary>نتیجه صدور موفق یک نامه.</summary>
public class IssuedLetterResult
{
    public Letter Letter { get; set; } = null!;

    /// <summary>مسیر فایل Word تولیدشده.</summary>
    public string GeneratedDocxPath { get; set; } = string.Empty;

    /// <summary>مسیر PDF، در صورت تولید.</summary>
    public string? GeneratedPdfPath { get; set; }

    public bool Printed { get; set; }

    /// <summary>هشدارها، مثلاً «۲ فیلد بدون مقدار جایگزین شدند».</summary>
    public List<string> Warnings { get; } = new();
}
