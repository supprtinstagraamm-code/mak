using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Infrastructure;
using CertificateAutomation.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CertificateAutomation.NumberStamp;

/// <summary>
/// نقطهٔ شروع ابزار شناور. عمداً بسیار سبک است: فقط همان پایگاه‌دادهٔ برنامهٔ اصلی را باز
/// می‌کند (AddInfrastructure همان چیزی است که برنامهٔ اصلی هم استفاده می‌کند) و یک پنجرهٔ
/// کوچک شناور نشان می‌دهد. منطق شماره‌گذاری و ثبت اکسل هم همان سرویس‌های برنامهٔ اصلی است؛
/// اینجا کد تکراری نیست.
/// </summary>
public partial class App : System.Windows.Application
{
    // نام‌گذاری سراسری (Global\) تا هم بین چند نشست کاربر فعلی و هم بین کاربران مختلف
    // روی یک سرور ترمینال، از دو نمونه هم‌زمان جلوگیری شود.
    private const string SingleInstanceMutexName = "Global\\CertificateAutomation_NumberStamp_SingleInstance";

    private Mutex? _singleInstanceMutex;
    private ServiceProvider? _services;
    public static IServiceProvider? Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var culture = new CultureInfo("fa-IR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("fa-IR")));

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            // یک نمونه از ابزار شناور همین الان روی سیستم در حال اجراست؛ خروج بی‌صدا.
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                MessageBox.Show($"خطای غیرمنتظره در ابزار شناور:\n{args.Exception.Message}",
                    "ابزار شناور", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* ignore */ }
            args.Handled = true;
        };

        try
        {
            var services = new ServiceCollection();
            services.AddInfrastructure();
            _services = services.BuildServiceProvider();
            Services = _services;

            var init = _services.GetRequiredService<DbInitializer>().Initialize();
            if (init.IsFailure)
            {
                MessageBox.Show(
                    $"{init.Error}\n\nمسیر پایگاه داده: {AppPaths.DatabaseFile}\n\n" +
                    "ابزار شناور بدون پایگاه داده قابل استفاده نیست.",
                    "ابزار شناور", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            var settings = _services.GetRequiredService<ISettingsService>();
            settings.LoadAsync().GetAwaiter().GetResult();

            // ثبت اجرای خودکار با ویندوز (در صورتی که قبلاً ثبت نشده یا مسیر فایل عوض شده).
            NumberStampAutoStart.EnsureRegistered();

            var window = new MainWindow(_services);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ابزار شناور نتوانست شروع شود:\n{ex.Message}",
                "ابزار شناور", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
            catch { /* ignore */ }
        }
        base.OnExit(e);
    }
}
