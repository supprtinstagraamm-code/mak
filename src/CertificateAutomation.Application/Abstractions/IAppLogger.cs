namespace CertificateAutomation.Application.Abstractions;

/// <summary>
/// انتزاع لاگ‌نویسی تا لایه‌های بالاتر به Serilog وابسته نباشند.
/// </summary>
public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Debug(string message);
}
