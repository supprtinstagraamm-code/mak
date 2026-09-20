using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Domain.Common;
using CertificateAutomation.Domain.Entities;
using CertificateAutomation.Domain.Enums;

namespace CertificateAutomation.Infrastructure.Security;

/// <summary>
/// وضعیت ورود مدیر در جلسه جاری. پس از مدت مشخصی بی‌فعالیتی، دسترسی مدیر
/// به‌صورت خودکار قفل می‌شود تا رها کردن سیستم باز، خطرناک نباشد.
/// </summary>
public class UserSession : IUserSession
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;

    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public UserSession(IUserRepository users, IPasswordHasher hasher, ISettingsService settings, IAppLogger logger)
    {
        _users = users;
        _hasher = hasher;
        _settings = settings;
        _logger = logger;
    }

    public AppUser? CurrentUser { get; private set; }

    public bool IsAdmin
    {
        get
        {
            if (CurrentUser is null || CurrentUser.Role != UserRole.Admin) return false;

            var timeout = _settings.GetInt(SettingKeys.AdminLockMinutes, 15);
            if (timeout > 0 && (DateTime.UtcNow - _lastActivityUtc).TotalMinutes > timeout)
            {
                _logger.Info("جلسه مدیر به‌دلیل بی‌فعالیتی قفل شد.");
                Logout();
                return false;
            }
            return true;
        }
    }

    public string UserName => CurrentUser?.Username ?? "کاربر";

    public event EventHandler? SessionChanged;

    public async Task<Result> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Result.Failure("نام کاربری و رمز عبور را وارد کنید.");

        var user = await _users.FindByUsernameAsync(username.Trim(), ct);

        // پیام یکسان برای «کاربر نیست» و «رمز غلط» تا اطلاعات لو نرود.
        if (user is null || !user.IsActive || !_hasher.Verify(password, user.PasswordHash, user.Salt))
        {
            _logger.Warn($"تلاش ناموفق برای ورود با نام کاربری «{username}».");
            return Result.Failure("نام کاربری یا رمز عبور نادرست است.");
        }

        CurrentUser = user;
        _lastActivityUtc = DateTime.UtcNow;
        _logger.Info($"کاربر «{user.Username}» وارد شد.");
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return Result.Success();
    }

    public void Logout()
    {
        CurrentUser = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Touch() => _lastActivityUtc = DateTime.UtcNow;
}
