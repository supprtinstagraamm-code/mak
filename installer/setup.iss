; ============================================================
;  اسکریپت نصب سامانه اتوماسیون صدور گواهی — Inno Setup 6
;  نصب استاندارد. پیش‌نیاز: ابتدا build.ps1 اجرا شود (پوشهٔ publish ساخته شود).
;
;  ساخت فایل نصب:
;    فایل را در Inno Setup باز کنید و F9 بزنید، یا:  iscc setup.iss
;  خروجی:  dist\CertificateAutomation-Setup-1.0.0.exe
; ============================================================

#define AppName "سامانه اتوماسیون صدور گواهی"
#define AppVersion "1.0.0"
#define AppPublisher "شرکت شما"
#define AppExeName "CertificateAutomation.exe"

[Setup]
AppId={{8F2A6C41-7B3D-4E58-9C21-5D6E7A8B9C01}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\CertificateAutomation
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=CertificateAutomation-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64
; پیام خوش‌آمد فارسی
AppComments=نرم‌افزار صدور خودکار گواهی‌ها و نامه‌های اداری

[Languages]
Name: "farsi"; MessagesFile: "compiler:Default.isl"
; برای ویزارد کاملاً فارسی، فایل Persian.isl را از مخزن ترجمه‌های Inno Setup دریافت
; و در پوشهٔ Languages نصب Inno Setup قرار دهید، سپس خط بالا را به این تغییر دهید:
;   MessagesFile: "compiler:Languages\Persian.isl"

[Files]
; خروجی publish خوداتکا (شامل .NET و همهٔ وابستگی‌ها)
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; داده نمونه
Source: "..\SampleData\*"; DestDir: "{commonappdata}\CertificateAutomation\SampleData"; \
    Flags: ignoreversion recursesubdirs createallsubdirs
; قالب‌های نمونه مستقیماً در پوشهٔ فعال قالب‌ها (تا کاربر بلافاصله بتواند نامه صادر کند)
Source: "..\SampleData\Templates\*"; DestDir: "{commonappdata}\CertificateAutomation\Templates"; \
    Flags: ignoreversion recursesubdirs createallsubdirs onlyifdoesntexist
; راهنماها
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs

[Dirs]
; پوشهٔ داده‌ها برای همهٔ کاربران قابل نوشتن باشد (اجرا بدون دسترسی مدیر)
Name: "{commonappdata}\CertificateAutomation"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\Data"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\Templates"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\Output"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\Backups"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\logs"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\راهنمای استفاده"; Filename: "{app}\docs\راهنمای-استفاده.md"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "ایجاد میان‌بر روی میز کار"; GroupDescription: "میان‌برها:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "اجرای برنامه"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; پایگاه داده و سوابق عمداً حذف نمی‌شوند تا اطلاعات کاربر از بین نرود.
Type: filesandordirs; Name: "{commonappdata}\CertificateAutomation\temp"
