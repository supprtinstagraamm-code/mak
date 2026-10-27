using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CertificateAutomation.Application.Abstractions;
using CertificateAutomation.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CertificateAutomation.NumberStamp;

/// <summary>
/// پنجرهٔ شناور. دو حالت دارد: دکمهٔ گرد کوچک (پیش‌فرض) و پنل بازشدهٔ ثبت شماره.
/// جابه‌جایی با کشیدن دکمهٔ گرد انجام می‌شود؛ یک کلیک ساده (بدون جابه‌جایی) پنل را باز می‌کند.
/// </summary>
public partial class MainWindow : Window
{
    private const double CollapsedSize = 54;
    private const double ClickMoveThreshold = 4;

    private readonly INumberGenerator _numbers;
    private readonly ILetterIssueService _issueService;
    private readonly ISettingsService _settings;
    private readonly IAppLogger _logger;

    private double _anchorLeft;
    private double _anchorTop;
    private bool _isBusy;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();

        _numbers = services.GetRequiredService<INumberGenerator>();
        _issueService = services.GetRequiredService<ILetterIssueService>();
        _settings = services.GetRequiredService<ISettingsService>();
        _logger = services.GetRequiredService<IAppLogger>();

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;

        var savedX = _settings.GetInt(SettingKeys.NumberStampPosX, -1);
        var savedY = _settings.GetInt(SettingKeys.NumberStampPosY, -1);

        if (savedX >= 0 && savedY >= 0)
        {
            Left = savedX;
            Top = savedY;
        }
        else
        {
            // پیش‌فرض: گوشهٔ پایین-راست صفحه، کمی بالاتر از نوار وظیفه.
            Left = workArea.Right - CollapsedSize - 24;
            Top = workArea.Bottom - CollapsedSize - 24;
        }

        ClampToWorkArea();
    }

    // ---------- کشیدن / کلیک روی دکمهٔ گرد ----------

    private void CollapsedButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var startLeft = Left;
        var startTop = Top;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove گاهی اگر دکمهٔ موس قبلاً رها شده باشد استثنا می‌دهد؛ بی‌خطر نادیده گرفته می‌شود.
        }

        var moved = Math.Abs(Left - startLeft) > ClickMoveThreshold || Math.Abs(Top - startTop) > ClickMoveThreshold;

        if (moved)
        {
            ClampToWorkArea();
            SavePosition();
        }
        else
        {
            ExpandPanel();
        }
    }

    private void CollapsedButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowContextMenu();
    }

    private void ShowContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu { FlowDirection = FlowDirection.RightToLeft };

        var openExcel = new System.Windows.Controls.MenuItem { Header = "باز کردن فایل اکسل ثبت‌ها" };
        openExcel.Click += (_, _) => OpenManualNumbersExcel();
        menu.Items.Add(openExcel);

        var autoStart = new System.Windows.Controls.MenuItem
        {
            Header = "اجرای خودکار با ویندوز",
            IsCheckable = true,
            IsChecked = NumberStampAutoStart.IsEnabled()
        };
        autoStart.Click += (_, _) => NumberStampAutoStart.SetEnabled(autoStart.IsChecked);
        menu.Items.Add(autoStart);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exit = new System.Windows.Controls.MenuItem { Header = "خروج از ابزار شناور" };
        exit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(exit);

        menu.IsOpen = true;
    }

    private void OpenManualNumbersExcel()
    {
        try
        {
            if (!File.Exists(AppPaths.ManualNumbersExcelFile))
            {
                MessageBox.Show("هنوز هیچ شماره‌ای از این ابزار ثبت نشده؛ فایل اکسل ساخته نشده است.",
                    "ابزار شناور", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo(AppPaths.ManualNumbersExcelFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warn($"باز کردن فایل اکسل ناموفق بود: {ex.Message}");
            MessageBox.Show($"باز کردن فایل ممکن نشد:\n{ex.Message}", "ابزار شناور",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- باز/بسته کردن پنل ----------

    private async void ExpandPanel()
    {
        _anchorLeft = Left;
        _anchorTop = Top;

        CollapsedButton.Visibility = Visibility.Collapsed;
        ExpandedPanel.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
        DescriptionBox.Text = string.Empty;
        NextNumberText.Text = "…";

        RepositionAfterLayout();
        DescriptionBox.Focus();

        try
        {
            var next = await _numbers.PeekNextAsync();
            NextNumberText.Text = next;
        }
        catch (Exception ex)
        {
            NextNumberText.Text = "نامشخص";
            _logger.Warn($"دریافت شمارهٔ بعدی ناموفق بود: {ex.Message}");
        }
    }

    private void CollapsePanel()
    {
        if (_isBusy) return;

        ExpandedPanel.Visibility = Visibility.Collapsed;
        CollapsedButton.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;

        Left = _anchorLeft;
        Top = _anchorTop;
    }

    /// <summary>بعد از تغییر محتوا، اندازهٔ پنجره در پاس بعدی Layout عوض می‌شود؛
    /// این متد بعد از آن، گوشهٔ پایین-راست پنل را دقیقاً روی گوشهٔ پایین-راست دکمهٔ قبلی می‌گذارد.</summary>
    private void RepositionAfterLayout()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var bottomRight = new Point(_anchorLeft + CollapsedSize, _anchorTop + CollapsedSize);
            var newLeft = bottomRight.X - ActualWidth;
            var newTop = bottomRight.Y - ActualHeight;

            var workArea = SystemParameters.WorkArea;
            if (newLeft < workArea.Left) newLeft = workArea.Left + 8;
            if (newTop < workArea.Top) newTop = workArea.Top + 8;
            if (newLeft + ActualWidth > workArea.Right) newLeft = workArea.Right - ActualWidth - 8;
            if (newTop + ActualHeight > workArea.Bottom) newTop = workArea.Bottom - ActualHeight - 8;

            Left = newLeft;
            Top = newTop;
        }), DispatcherPriority.Loaded);
    }

    private void CloseIcon_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => CollapsePanel();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => CollapsePanel();

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (ExpandedPanel.Visibility == Visibility.Visible)
            CollapsePanel();
    }

    // ---------- ثبت شماره ----------

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var description = DescriptionBox.Text?.Trim() ?? string.Empty;

        try
        {
            _isBusy = true;
            SubmitButton.IsEnabled = false;
            ShowStatus("در حال ثبت...", isError: false);

            var result = await _issueService.IssueManualNumberAsync(description, Environment.UserName);

            if (result.IsFailure)
            {
                ShowStatus(result.Error ?? "ثبت شماره ناموفق بود.", isError: true);
                return;
            }

            var number = result.Value.LetterNumber;
            var msg = $"✓ شمارهٔ {number} ثبت شد.";
            if (!string.IsNullOrWhiteSpace(result.Value.ExcelWarning))
                msg += "\n⚠ " + result.Value.ExcelWarning;

            ShowStatus(msg, isError: !string.IsNullOrWhiteSpace(result.Value.ExcelWarning));

            await Task.Delay(1600);

            // CollapsePanel خودش وقتی _isBusy=true باشد کاری نمی‌کند؛ قبل از فراخوانی آزادش می‌کنیم.
            _isBusy = false;
            if (ExpandedPanel.Visibility == Visibility.Visible)
                CollapsePanel();
        }
        catch (Exception ex)
        {
            _logger.Error("ثبت دستی شماره از ابزار شناور ناموفق بود.", ex);
            ShowStatus($"خطای غیرمنتظره: {ex.Message}", isError: true);
        }
        finally
        {
            _isBusy = false;
            SubmitButton.IsEnabled = true;
        }
    }

    private void ShowStatus(string text, bool isError)
    {
        StatusText.Text = text;
        StatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xC0, 0x2B, 0x2B))
            : new SolidColorBrush(Color.FromRgb(0x1E, 0x8A, 0x4C));
        StatusText.Visibility = Visibility.Visible;
    }

    // ---------- کمکی ----------

    private void ClampToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        if (Left < workArea.Left) Left = workArea.Left;
        if (Top < workArea.Top) Top = workArea.Top;
        if (Left + CollapsedSize > workArea.Right) Left = workArea.Right - CollapsedSize;
        if (Top + CollapsedSize > workArea.Bottom) Top = workArea.Bottom - CollapsedSize;
    }

    private void SavePosition() => _ = SavePositionAsync();

    private async Task SavePositionAsync()
    {
        try
        {
            await _settings.SetManyAsync(new Dictionary<string, string>
            {
                [SettingKeys.NumberStampPosX] = ((int)Left).ToString(),
                [SettingKeys.NumberStampPosY] = ((int)Top).ToString()
            });
        }
        catch (Exception ex)
        {
            _logger.Warn($"ذخیرهٔ موقعیت دکمهٔ شناور ناموفق بود: {ex.Message}");
        }
    }
}
