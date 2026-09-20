namespace CertificateAutomation.Domain.Common;

/// <summary>
/// اعتبارسنجی کد ملی ایران (۱۰ رقم با رقم کنترلی). همان الگوریتمی که در بارگذاری Excel
/// استفاده می‌شود، اینجا هم در دسترس است تا در بخش «سلامت داده‌ها» هم استفاده شود.
/// </summary>
public static class IranianNationalId
{
    public static bool IsValid(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        code = code.Trim();

        if (code.Length != 10 || !code.All(char.IsDigit)) return false;
        if (new string(code[0], 10) == code) return false; // ارقام یکسان

        var sum = 0;
        for (var i = 0; i < 9; i++)
            sum += (code[i] - '0') * (10 - i);

        var remainder = sum % 11;
        var check = code[9] - '0';
        return remainder < 2 ? check == remainder : check == 11 - remainder;
    }
}
