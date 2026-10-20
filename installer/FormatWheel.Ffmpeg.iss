; Format Wheel - FFmpeg extension installer.
; Installs the extension DLL into the app's extensions folder and makes sure FFmpeg is present (via winget).

#define ExtName    "Format Wheel - FFmpeg"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{F7A1C4E2-8B3D-4E6F-9A2C-FF1B2E3D4C50}
AppName={#ExtName}
AppVersion={#AppVersion}
AppVerName={#ExtName} {#AppVersion}
AppPublisher=Format Wheel

PrivilegesRequired=lowest
; extensions live next to the app, each in its own subfolder
DefaultDirName={localappdata}\Programs\FormatWheel\extensions\ffmpeg
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

OutputDir=..\dist
OutputBaseFilename=FormatWheel-FFmpeg-Setup-{#AppVersion}
UninstallDisplayName={#ExtName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.NeedApp=Format Wheel is not installed. Install the app first, then this extension.
ru.NeedApp=Format Wheel не установлен. Сначала установите программу, затем это расширение.
en.InstallingFfmpeg=Installing FFmpeg via winget (this can take a minute)...
ru.InstallingFfmpeg=Устанавливаю FFmpeg через winget (может занять минуту)...
en.NoWinget=winget is not available. Install FFmpeg manually: winget install Gyan.FFmpeg
ru.NoWinget=winget недоступен. Установите FFmpeg вручную: winget install Gyan.FFmpeg

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
// без приложения расширение бессмысленно
function InitializeSetup(): Boolean;
begin
  if not DirExists(ExpandConstant('{localappdata}\Programs\FormatWheel')) then
  begin
    MsgBox(ExpandConstant('{cm:NeedApp}'), mbError, MB_OK);
    Result := False;
  end
  else
    Result := True;
end;

function WingetPath(): String;
var sys: String;
begin
  // winget лежит в WindowsApps, через {sys} не найти — пробуем прямой путь и просто имя
  Result := 'winget';
end;

procedure InstallFfmpeg();
var code: Integer;
begin
  WizardForm.StatusLabel.Caption := ExpandConstant('{cm:InstallingFfmpeg}');
  if not Exec(ExpandConstant('{cmd}'), '/c winget install --id Gyan.FFmpeg -e --accept-source-agreements --accept-package-agreements',
       '', SW_HIDE, ewWaitUntilTerminated, code) then
    MsgBox(ExpandConstant('{cm:NoWinget}'), mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // ставим FFmpeg только если его ещё нет
    if not Exec(ExpandConstant('{cmd}'), '/c where ffmpeg', '', SW_HIDE, ewWaitUntilTerminated, code) or (code <> 0) then
      InstallFfmpeg();
  end;
end;
