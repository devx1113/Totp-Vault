; TOTP Vault インストーラー（Inno Setup 6）
; 作成方法: dotnet publish 後に  ISCC.exe installer\TotpVault.iss
#define AppName "TOTP Vault"
#define AppVersion "1.0.0"
#define AppExe "TotpVault.exe"

[Setup]
AppId={{6E2C9B1A-4F7D-4C1E-9A55-2B8D7E3F1C60}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DevX
AppCopyright=Copyright (C) 2026 DevX
DefaultDirName={autopf}\TOTP Vault
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\artifacts\installer
OutputBaseFilename=TotpVault-Setup-{#AppVersion}
SetupIconFile=..\src\TotpVault.App\Assets\app.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.14393
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; 注意: アンインストール時も保管庫データ（%LOCALAPPDATA%\TotpVault）は削除しません。
; 誤ってアンインストールした場合に認証情報を失わないための方針です。
