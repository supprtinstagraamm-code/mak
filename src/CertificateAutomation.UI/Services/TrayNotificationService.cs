using System.Windows;
using System.Windows.Threading;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.UI.ViewModels;
using CertificateAutomation.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CertificateAutomation.UI.Services;

/// <summary>
/// آیکون سینی سیستم (System Tray) و اعلان یادآورها.
///
/// نکتهٔ مهم دربارهٔ محدودیت این روش: اعلان‌ها فقط تا وقتی برنامه در حال اجراست کار می‌کنند —
/// حتی اگر پنجره بسته/مخفی باشد (چون با زدن ضربدر، برنامه واقعاً بسته نمی‌شود بلکه به سینی
/// سیستم می‌رود). اگر کاربر برنامه را کاملاً از Task Manager ببندد یا سیستم را خاموش کند،
/// تا اجرای بعدی برنامه هیچ اعلانی نمی‌آید. برای اطمینان کامل، تنظیم «اجرای خودکار هنگام
/// روشن شدن ویندوز» در صفحهٔ تنظیمات را فعال کنید تا برنامه همیشه در پس‌زمینه باشد.
/// </summary>
public class TrayNotificationService : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IAppLogger _logger;
    private readonly ShellWindow _window;

    private System.Windows.Forms.NotifyIcon? _icon;
    private DispatcherTimer? _timer;
    private bool _backgroundHintShown;

    public TrayNotificationService(ShellWindow window, IServiceProvider services, IAppLogger logger)
    {
        _window = window;
        _services = services;
        _logger = logger;
    }

    public void Start()
    {
        try
        {
            SetupIcon();
            SetupCloseToTrayBehavior();
            SetupTimer();

            // یک بررسی فوری، چند ثانیه پس از بالا آمدن برنامه
            _ = CheckDueRemindersAsync();
        }
        catch (Exception ex)
        {
            // اگر آیکون سینی به هر دلیلی روی این سیستم کار نکرد، نباید کل برنامه را متوقف کند.
            _logger.Warn($"راه‌اندازی آیکون سینی سیستم ناموفق بود: {ex.Message}");
        }
    }

    private void SetupIcon()
    {
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "سامانه اتوماسیون صدور گواهی",
            Visible = true
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("باز کردن برنامه", null, (_, _) => RestoreWindow());
        menu.Items.Add("یادآورهای امروز", null, (_, _) => { RestoreWindow(); NavigateToReminders(); });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("خروج کامل", null, (_, _) => ExitApplication());
        _icon.ContextMenuStrip = menu;

        _icon.DoubleClick += (_, _) => RestoreWindow();
        _icon.BalloonTipClicked += (_, _) => { RestoreWindow(); NavigateToReminders(); };
    }

    /// <summary>با زدن ضربدر پنجره، برنامه بسته نمی‌شود؛ به سینی سیستم می‌رود.</summary>
    private void SetupCloseToTrayBehavior()
    {
        _window.Closing += (_, e) =>
        {
            if (_window.AllowClose) return; // خروج واقعی (از دکمهٔ «خروج» یا منوی سینی)

            e.Cancel = true;
            _window.Hide();

            if (!_backgroundHintShown)
            {
                _backgroundHintShown = true;
                _icon?.ShowBalloonTip(4000, "برنامه در پس‌زمینه اجرا می‌ماند",
                    "یادآورها همچنان بررسی می‌شوند. برای خروج کامل، از منوی راست‌کلیک روی آیکون سینی سیستم استفاده کنید.",
                    System.Windows.Forms.ToolTipIcon.Info);
            }
        };
    }

    private void SetupTimer()
    {
        // هر ۳۰ دقیقه بررسی می‌شود؛ چون یادآور فقط روزانه یک بار معنا دارد، این فاصله کافی و سبک است.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _timer.Tick += async (_, _) => await CheckDueRemindersAsync();
        _timer.Start();
    }

    private async Task CheckDueRemindersAsync()
    {
        try
        {
            using var scope = _services.CreateScope();
            var reminderService = scope.ServiceProvider.GetRequiredService<IReminderService>();
            var due = await reminderService.GetDueForNotificationAsync();

            foreach (var r in due)
            {
                _icon?.ShowBalloonTip(8000, $"یادآور: {r.Title}",
                    string.IsNullOrWhiteSpace(r.Description)
                        ? $"امروز روز {r.DayOfMonth} است — نوبت این یادآور رسیده."
                        : r.Description,
                    System.Windows.Forms.ToolTipIcon.Info);

                _logger.Info($"اعلان یادآور «{r.Title}» نمایش داده شد.");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"بررسی یادآورهای سررسیده ناموفق بود: {ex.Message}");
        }
    }

    private void RestoreWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void NavigateToReminders()
    {
        if (_window.DataContext is ShellViewModel vm && vm.NavigateCommand.CanExecute("reminders"))
            vm.NavigateCommand.Execute("reminders");
    }

    private void ExitApplication()
    {
        _window.AllowClose = true;
        System.Windows.Application.Current?.Shutdown();
    }

    public void Dispose()
    {
        _timer?.Stop();
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
