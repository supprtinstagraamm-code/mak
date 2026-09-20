using CertificateAutomation.Domain.Enums;

namespace CertificateAutomation.Domain.Entities;

/// <summary>سابقه یک بار چاپ (یا ذخیره PDF) از یک نامه.</summary>
public class PrintJob
{
    public long Id { get; set; }
    public long LetterId { get; set; }
    public string? PrinterName { get; set; }
    public int Copies { get; set; } = 1;
    public DateTime PrintedAt { get; set; }
    public PrintStatus Status { get; set; }
    public string? ErrorText { get; set; }
}
