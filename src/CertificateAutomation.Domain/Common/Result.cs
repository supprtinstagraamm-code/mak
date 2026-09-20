namespace CertificateAutomation.Domain.Common;

/// <summary>
/// نتیجه یک عملیات، بدون استفاده از Exception برای خطاهای قابل انتظار.
/// پیام خطا همیشه فارسی و قابل نمایش به کاربر است.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    /// <summary>پیام خطای فارسی و قابل نمایش به کاربر.</summary>
    public string? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);

    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);
    public static Result<T> Failure<T>(string error) => Result<T>.Fail(error);
}

/// <summary>نتیجه عملیاتی که مقدار برمی‌گرداند.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, string? error) : base(isSuccess, error)
        => _value = value;

    /// <summary>مقدار نتیجه. فقط در صورت موفقیت معتبر است.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("دسترسی به مقدار یک نتیجه ناموفق مجاز نیست.");

    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}
