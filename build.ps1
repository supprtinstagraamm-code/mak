# ============================================================
#  اسکریپت ساخت خودکار نسخهٔ نهایی
#  این اسکریپت را روی «سیستم توسعه» (با اینترنت و .NET 8 SDK) اجرا کنید.
#  خروجی: یک پوشهٔ publish با فایل اجرایی خوداتکا، آمادهٔ ساخت فایل نصب.
#
#  روش اجرا: در PowerShell از ریشهٔ پروژه:
#      .\build.ps1
#  یا برای سیستم ۳۲ بیتی:
#      .\build.ps1 -Arch win-x86
# ============================================================

param(
    [string]$Arch = "win-x64",          # win-x64 برای ۶۴ بیتی، win-x86 برای ۳۲ بیتی
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host " ساخت نسخهٔ نهایی سامانه اتوماسیون صدور گواهی" -ForegroundColor Cyan
Write-Host " معماری: $Arch" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host ""

# بررسی نصب .NET SDK
Write-Host "[1/5] بررسی .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "      .NET SDK نسخهٔ $dotnetVersion یافت شد." -ForegroundColor Green
} catch {
    Write-Host "      خطا: .NET 8 SDK نصب نیست." -ForegroundColor Red
    Write-Host "      از این آدرس دانلود و نصب کنید: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Red
    exit 1
}

# پاک‌سازی خروجی قبلی
Write-Host ""
Write-Host "[2/5] پاک‌سازی خروجی‌های قبلی..." -ForegroundColor Yellow
if (Test-Path "publish") { Remove-Item "publish" -Recurse -Force }
Get-ChildItem -Recurse -Directory -Include bin, obj -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "      انجام شد." -ForegroundColor Green

# بازیابی بسته‌ها
Write-Host ""
Write-Host "[3/5] بازیابی بسته‌های NuGet (نیازمند اینترنت)..." -ForegroundColor Yellow
dotnet restore
Write-Host "      انجام شد." -ForegroundColor Green

# ساخت نسخهٔ خوداتکا — برنامهٔ اصلی
Write-Host ""
Write-Host "[4/5] ساخت برنامهٔ اصلی (ممکن است چند دقیقه طول بکشد)..." -ForegroundColor Yellow
dotnet publish src\CertificateAutomation.UI\CertificateAutomation.UI.csproj `
    -c $Configuration `
    -r $Arch `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish

# ساخت نسخهٔ خوداتکا — ابزار شناور ثبت شماره (فایل اجرایی جدا، کنار برنامهٔ اصلی)
Write-Host ""
Write-Host "[5/5] ساخت ابزار شناور (NumberStamp.exe)..." -ForegroundColor Yellow
dotnet publish src\CertificateAutomation.NumberStamp\CertificateAutomation.NumberStamp.csproj `
    -c $Configuration `
    -r $Arch `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o publish

Write-Host ""
Write-Host "======================================================" -ForegroundColor Green
Write-Host " ساخت با موفقیت انجام شد!" -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green
Write-Host ""
Write-Host " فایل‌های اجرایی در پوشهٔ publish ساخته شدند:" -ForegroundColor White
Write-Host "   publish\CertificateAutomation.exe   (برنامهٔ اصلی)" -ForegroundColor Cyan
Write-Host "   publish\NumberStamp.exe             (ابزار شناور ثبت شماره)" -ForegroundColor Cyan
Write-Host ""
Write-Host " گام بعدی — ساخت فایل نصب:" -ForegroundColor White
Write-Host "   فایل installer\setup.iss را با Inno Setup باز کنید و Compile (F9) بزنید." -ForegroundColor Cyan
Write-Host "   یا اگر Inno Setup در PATH است:" -ForegroundColor White
Write-Host "   iscc installer\setup.iss" -ForegroundColor Cyan
Write-Host ""
Write-Host " برای تست سریع همین حالا:" -ForegroundColor White
Write-Host "   .\publish\CertificateAutomation.exe" -ForegroundColor Cyan
Write-Host "   .\publish\NumberStamp.exe" -ForegroundColor Cyan
Write-Host ""
Write-Host " برای استفادهٔ قابل‌حمل (بدون نصب):" -ForegroundColor White
Write-Host "   کل پوشهٔ publish را روی فلش یا هر پوشه‌ای کپی کنید و از همان‌جا اجرا کنید." -ForegroundColor Cyan
Write-Host ""
