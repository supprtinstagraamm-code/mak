; ============================================================
;  نسخهٔ «همه‌چیز در یک فایل» — به‌همراه نصب خودکار LibreOffice
;
;  این نسخه برای سیستم‌های آفلاینی است که می‌خواهید کاربر فقط یک فایل اجرا کند
;  و برای خروجی PDF نیازی به نصب جداگانهٔ LibreOffice نباشد.
;
;  پیش از ساخت این نسخه:
;    ۱. build.ps1 را اجرا کنید (پوشهٔ publish ساخته شود).
;    ۲. نصاب آفلاین LibreOffice (فایل .msi) را دانلود کنید از:
;       https://www.libreoffice.org/download/download/
;       (نسخهٔ Windows x86_64، فرمت MSI)
;    ۳. فایل MSI دانلودشده را در پوشهٔ installer\redist با این نام قرار دهید:
;       LibreOffice.msi
;    ۴. این اسکریپت را با Inno Setup باز کنید و F9 بزنید.
;
;  خروجی:  dist\CertificateAutomation-Full-Setup-1.0.0.exe  (حجم بالا، ~۴۵۰MB)
; ============================================================

#define AppName "سامانه اتوماسیون صدور گواهی"
#define AppVersion "1.0.0"
#define AppPublisher "شرکت شما"
#define AppExeName "CertificateAutomation.exe"

[Setup]
AppId={{8F2A6C41-7B3D-4E58-9C21-5D6E7A8B9C02}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\CertificateAutomation
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=CertificateAutomation-Full-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "farsi"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\SampleData\*"; DestDir: "{commonappdata}\CertificateAutomation\SampleData"; \
    Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\SampleData\Templates\*"; DestDir: "{commonappdata}\CertificateAutomation\Templates"; \
    Flags: ignoreversion recursesubdirs createallsubdirs onlyifdoesntexist
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs
; نصاب LibreOffice (باید در redist\LibreOffice.msi موجود باشد)
Source: "redist\LibreOffice.msi"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Dirs]
Name: "{commonappdata}\CertificateAutomation"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\Data"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\Templates"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\Output"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\Backups"; Permissions: users-modify
Name: "{commonappdata}\CertificateAutomation\logs"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "ایجاد میان‌بر روی میز کار"; GroupDescription: "میان‌برها:"
Name: "installlibre"; Description: "نصب LibreOffice برای خروجی PDF و چاپ (توصیه‌شده)"; GroupDescription: "اجزای جانبی:"

[Run]
; نصب بی‌صدای LibreOffice در صورت انتخاب کاربر
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\LibreOffice.msi"" /qn /norestart"; \
    StatusMsg: "در حال نصب LibreOffice... (ممکن است چند دقیقه طول بکشد)"; \
    Tasks: installlibre; Flags: waituntilterminated
Filename: "{app}\{#AppExeName}"; Description: "اجرای برنامه"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\CertificateAutomation\temp"
