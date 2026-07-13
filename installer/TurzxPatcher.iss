; ============================================================================
; TurzxPatcher + TurzxSensorBridge Installer
; ============================================================================
; Combines both projects into a single installer:
;   - TurzxPatcher.exe     -> next to TURZX.exe
;   - patches\PatchModule.dll + patches\SensorService\* -> TURZX directory
;   - SensorConfig.exe     -> Program Files\TurzxSensorBridge (Start Menu entry)
;
; Features:
;   - TURZX path auto-detection (registry + common paths + browse)
;   - .NET Framework 4.8 check
;   - Admin privileges required
;   - Optional Start Menu shortcut for SensorConfig
;   - Optional Desktop shortcut for TurzxPatcher
;
; Build:  powershell -ExecutionPolicy Bypass -File installer\build.ps1
; Manual: ISCC.exe installer\TurzxPatcher.iss
; ============================================================================

[Setup]
AppName=TurzxPatcher + TurzxSensorBridge
AppVersion=2.1.0
AppPublisher=breacasu
AppPublisherURL=https://github.com/breacasu
AppSupportURL=https://github.com/breacasu/TurzxPatcher/issues
AppUpdatesURL=https://github.com/breacasu/TurzxPatcher/releases
LicenseFile=..\LICENSE
DefaultDirName={commonpf}\TurzxSensorBridge
DefaultGroupName=TurzxSensorBridge
OutputDir=output
OutputBaseFilename=TurzxPatcherSetup-{#SetupSetting("AppVersion")}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
DisableProgramGroupPage=no
UninstallDisplayIcon={app}\SensorConfig\SensorConfig.exe

; .NET 4.8 detection: Release >= 528040 means 4.8+ on Windows 10/11
[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Types]
Name: "full";   Description: "Full installation (TurzxPatcher + TurzxSensorBridge)"
Name: "patcher"; Description: "TurzxPatcher only (no sensor support)"
Name: "custom";  Description: "Custom"; Flags: iscustom

[Components]
Name: "patcher";     Description: "TurzxPatcher (A088 display patch + plugin host)"; Types: full patcher custom; Flags: fixed
Name: "sensorbridge"; Description: "TurzxSensorBridge (custom hardware sensors for TURZX)"; Types: full custom

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut for TurzxPatcher"; GroupDescription: "Additional shortcuts:"
Name: "startmenu";   Description: "Create a &Start Menu shortcut for SensorConfig"; GroupDescription: "Additional shortcuts:"

[Files]
; --- TurzxPatcher (always installed, goes into TURZX dir) ---
Source: "build\TurzxPatcher.exe";        DestDir: "{code:GetTurzxDir}"; Components: patcher; Flags: ignoreversion
Source: "build\TurzxPatcher.exe.config"; DestDir: "{code:GetTurzxDir}"; Components: patcher; Flags: ignoreversion onlyifdoesntexist

; --- PatchModule (plugin DLL, goes into TURZX\patches\) ---
Source: "build\patches\PatchModule.dll"; DestDir: "{code:GetTurzxDir}\patches"; Components: sensorbridge; Flags: ignoreversion

; --- SensorService (goes into TURZX\patches\SensorService\) ---
Source: "build\patches\SensorService\*"; DestDir: "{code:GetTurzxDir}\patches\SensorService"; Components: sensorbridge; Flags: ignoreversion recursesubdirs

; --- SensorConfig (standalone WPF app, goes into Program Files) ---
Source: "build\SensorConfig\*"; DestDir: "{app}\SensorConfig"; Components: sensorbridge; Flags: ignoreversion recursesubdirs

[Icons]
; Start Menu: SensorConfig
Name: "{group}\SensorConfig"; Filename: "{app}\SensorConfig\SensorConfig.exe"; Components: sensorbridge; Tasks: startmenu
Name: "{group}\Uninstall TurzxSensorBridge"; Filename: "{uninstallexe}"
; Desktop: TurzxPatcher (in TURZX dir)
Name: "{commondesktop}\TurzxPatcher"; Filename: "{code:GetTurzxDir}\TurzxPatcher.exe"; Components: patcher; Tasks: desktopicon

[Run]
; Launch TurzxPatcher after install (optional checkbox)
Filename: "{code:GetTurzxDir}\TurzxPatcher.exe"; Description: "Launch TurzxPatcher now"; Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallDelete]
Type: filesandordirs; Name: "{app}\SensorConfig"

[Code]
// ---------------------------------------------------------------------------
// .NET Framework 4.8 detection
// ---------------------------------------------------------------------------
function IsDotNet48Installed: Boolean;
var
  releaseKey: Cardinal;
begin
  Result := False;
  // Release >= 528040 = .NET 4.8 on Windows 10 1809+ / Windows 11
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', releaseKey) then
  begin
    if releaseKey >= 528040 then
      Result := True;
  end;
end;

// ---------------------------------------------------------------------------
// TURZX directory auto-detection
// ---------------------------------------------------------------------------
var
  TurzxDirPage: TInputDirWizardPage;

function FindTurzxDir: String;
var
  regPath: String;
begin
  Result := '';

  // 1. Try registry (TURZX might register its install path)
  if RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\TURZX', 'InstallLocation', regPath) then
  begin
    if FileExists(AddBackslash(regPath) + 'TURZX.exe') then
    begin
      Result := regPath;
      Exit;
    end;
  end;
  if RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TURZX', 'InstallLocation', regPath) then
  begin
    if FileExists(AddBackslash(regPath) + 'TURZX.exe') then
    begin
      Result := regPath;
      Exit;
    end;
  end;

  // 2. Try common installation paths
  if FileExists('C:\Program Files\TURZX\TURZX.exe') then
    Result := 'C:\Program Files\TURZX'
  else if FileExists('C:\Program Files (x86)\TURZX\TURZX.exe') then
    Result := 'C:\Program Files (x86)\TURZX'
  else if FileExists('C:\TURZX\TURZX.exe') then
    Result := 'C:\TURZX'
  else if FileExists(ExpandConstant('{userdocs}') + '\Universal-Screen-Themes\TURZX-V3.1.0-ENG\TURZX.exe') then
    Result := ExpandConstant('{userdocs}') + '\Universal-Screen-Themes\TURZX-V3.1.0-ENG';
end;

// ---------------------------------------------------------------------------
// Script entry points
// ---------------------------------------------------------------------------
function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  if not IsDotNet48Installed() then
  begin
    if MsgBox('.NET Framework 4.8 is not installed.' #13#10 #13#10
              'TURZX and all TurzxPatcher components require .NET Framework 4.8.' #13#10
              'Do you want to open the download page now?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    Result := False;
    Exit;
  end;
end;

procedure InitializeWizard;
var
  detectedDir: String;
begin
  detectedDir := FindTurzxDir();
  if detectedDir = '' then
    detectedDir := 'C:\Program Files\TURZX';

  TurzxDirPage := CreateInputDirPage(wpSelectDir,
    'Select TURZX Installation Directory',
    'TurzxPatcher.exe and the patches\ folder must be placed inside your TURZX installation directory (where TURZX.exe is located).',
    'TURZX installation directory:',
    False,
    '');
  TurzxDirPage.Add('');
  TurzxDirPage.Values[0] := detectedDir;
end;

function GetTurzxDir(Param: String): String;
begin
  if Assigned(TurzxDirPage) then
    Result := TurzxDirPage.Values[0]
  else
    Result := 'C:\Program Files\TURZX';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  // Skip the default Program Files directory page — we use our custom TURZX dir page
  if PageID = wpSelectDir then
    Result := True
  else
    Result := False;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoTypeInfo + MemoComponentsInfo + NewLine +
            'TURZX directory:' + Space + GetTurzxDir('') + NewLine + NewLine +
            MemoGroupInfo + MemoTasksInfo;
end;
