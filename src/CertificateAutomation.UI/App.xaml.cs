using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Infrastructure;
using CertificateAutomation.Infrastructure.Data;
using CertificateAutomation.UI.Converters;
using CertificateAutomation.UI.Services;
using CertificateAutomation.UI.ViewModels;
using CertificateAutomation.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CertificateAutomation.UI;

/// <summary>
/// نقطه شروع برنامه: ساخت کانتینر تزریق وابستگی، آماده‌سازی پایگاه داده،
/// بارگذاری تنظیمات و نمایش پنجره اصلی.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private IAppLogger? _logger;
    private Services.TrayNotificationService? _tray;

    /// <summary>دسترسی View‌ها به کانتینر برای مواردی مثل ساخت دیالوگ ورود.</summary>
    public static IServiceProvider? Services { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // تمام متن‌ها با فرهنگ فارسی رندر شوند (تاریخ، ترتیب حروف و ...)
        var culture = new CultureInfo("fa-IR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("fa-IR")));

        RegisterGlobalErrorHandlers();

        try
        {
            _services = BuildServiceProvider();
            Services = _services;
            _logger = _services.GetRequiredService<IAppLogger>();
            _logger.Info("برنامه شروع شد.");

            // ۱) ساخت یا ارتقای پایگاه داده
            var init = _services.GetRequiredService<DbInitializer>().Initialize();
            if (init.IsFailure)
            {
                ShowError($"{init.Error}\n\nمسیر پایگاه داده: {AppPaths.DatabaseFile}");
                Shutdown(1);
                return;
            }

            // ۲) بارگذاری تنظیمات
            var settings = _services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync();

            PersianDigitsConverter.Enabled = settings.GetBool(SettingKeys.UsePersianDigits, true);

            // ۳) اعمال تم ذخیره‌شده
            _services.GetRequiredService<ThemeManager>().ApplySaved();

            // ۴) نمایش پنجره اصلی
            var shell = _services.GetRequiredService<ShellWindow>();
            MainWindow = shell;
            shell.Show();

            await ((ShellViewModel)shell.DataContext).InitializeAsync();

            // ۵) آیکون سینی سیستم و بررسی دوره‌ای یادآورها (حتی وقتی پنجره مخفی است)
            _tray = new TrayNotificationService(shell, _services, _logger);
            _tray.Start();
        }
        catch (Exception ex)
        {
            _logger?.Error("خطای بحرانی هنگام شروع برنامه.", ex);
            ShowError($"برنامه نتوانست شروع شود:\n{ex.Message}");
            Shutdown(1);
        }
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure();

        services.AddSingleton<ThemeManager>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<EmployeesViewModel>();
        services.AddSingleton<IssueLetterViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<TemplatesViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<StatisticsViewModel>();
        services.AddSingleton<RemindersViewModel>();
        services.AddSingleton<AdvancedSearchViewModel>();
        services.AddSingleton<ProjectComparisonViewModel>();
        services.AddSingleton<ActivityLogViewModel>();
        services.AddSingleton<DataQualityViewModel>();

        services.AddSingleton<ShellWindow>();

        return services.BuildServiceProvider();
    }

    /// <summary>هیچ خطای پیش‌بینی‌نشده‌ای نباید باعث بسته شدن ناگهانی برنامه شود.</summary>
    private void RegisterGlobalErrorHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.Error("خطای مدیریت‌نشده در دامنه برنامه.", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.Error("خطای مدیریت‌نشده در یک Task.", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("خطای مدیریت‌نشده در رابط کاربری.", e.Exception);

        ShowError(
            "یک خطای پیش‌بینی‌نشده رخ داد. برنامه بسته نمی‌شود، اما ممکن است آخرین عملیات کامل نشده باشد.\n\n" +
            $"شرح خطا: {e.Exception.Message}\n\n" +
            $"جزئیات کامل در فایل لاگ ثبت شد:\n{AppPaths.LogsFolder}");

        e.Handled = true;
    }

    private static void ShowError(string message)
        => MessageBox.Show(message, "خطا", MessageBoxButton.OK, MessageBoxImage.Error,
            MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("برنامه بسته شد.");
        _tray?.Dispose();
        (_logger as IDisposable)?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
