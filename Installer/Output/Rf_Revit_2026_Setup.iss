; ============================================================
;  Rf_Revit_2026 - Inno Setup Script
;  Rafi | Revit 2026 Plugin Installer
; ============================================================

#define MyAppName      "Rf Revit 2026 Plugin"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "Rafi"
#define RevitAddinDir  "{commonappdata}\Autodesk\Revit\Addins\2026"
#define PluginSubDir   "Rf_Revit_2026"
#define SourceBase     "C:\Users\Rafi\source\repos\Revit26_Plugin\bin\19\Release\net8.0-windows"

[Setup]
AppId={{a4e8c4c7-9f8b-4a6a-9d31-532f8c980d41}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={#RevitAddinDir}\{#PluginSubDir}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=C:\Users\Rafi\source\repos\Revit26_Plugin\Installer\Output
OutputBaseFilename=Rf_Revit_2026_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\Revit26_Plugin_Revised.dll
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Dirs]
Name: "{#RevitAddinDir}";                     Permissions: everyone-full
Name: "{#RevitAddinDir}\{#PluginSubDir}";     Permissions: everyone-full

; ============================================================
;  FILES
; ============================================================
[Files]
; --- Main plugin assembly ---
Source: "{#SourceBase}\Revit26_Plugin_Revised.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Revit26_Plugin_Revised.deps.json";    DestDir: "{app}"; Flags: ignoreversion

; --- CommunityToolkit ---
Source: "{#SourceBase}\CommunityToolkit.Mvvm.dll";   DestDir: "{app}"; Flags: ignoreversion

; --- ControlzEx ---
Source: "{#SourceBase}\ControlzEx.dll";              DestDir: "{app}"; Flags: ignoreversion


; --- MahApps.Metro ---
Source: "{#SourceBase}\MahApps.Metro.dll";           DestDir: "{app}"; Flags: ignoreversion

; --- MahApps IconPacks ---
Source: "{#SourceBase}\MahApps.Metro.IconPacks.dll";                      DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Core.dll";                 DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.BootstrapIcons.dll";       DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.BoxIcons.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.BoxIcons2.dll";            DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.CircumIcons.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Codicons.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Coolicons.dll";            DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Entypo.dll";               DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.EvaIcons.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.FeatherIcons.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.FileIcons.dll";            DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Fontaudio.dll";            DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.FontAwesome.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.FontAwesome5.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.FontAwesome6.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Fontisto.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.ForkAwesome.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.GameIcons.dll";            DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Ionicons.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.JamIcons.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.KeyruneIcons.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Lucide.dll";               DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Material.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.MaterialDesign.dll";       DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.MaterialLight.dll";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.MemoryIcons.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Microns.dll";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.MingCuteIcons.dll";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Modern.dll";               DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.MynaUIIcons.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Octicons.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.PhosphorIcons.dll";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.PicolIcons.dll";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.PixelartIcons.dll";        DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.RadixIcons.dll";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.RemixIcon.dll";            DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.RPGAwesome.dll";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.SimpleIcons.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Typicons.dll";             DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Unicons.dll";              DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.VaadinIcons.dll";          DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.WeatherIcons.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MahApps.Metro.IconPacks.Zondicons.dll";            DestDir: "{app}"; Flags: ignoreversion

; --- MaterialDesign ---
Source: "{#SourceBase}\MaterialDesignColors.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\MaterialDesignThemes.Wpf.dll";     DestDir: "{app}"; Flags: ignoreversion

; --- MIConvexHull ---
Source: "{#SourceBase}\MIConvexHull.dll";                 DestDir: "{app}"; Flags: ignoreversion

; --- Microsoft.Extensions ---
Source: "{#SourceBase}\Microsoft.Extensions.Configuration.Abstractions.dll";   DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Microsoft.Extensions.Configuration.dll";                DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Microsoft.Extensions.Configuration.FileExtensions.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Microsoft.Extensions.Configuration.Json.dll";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Microsoft.Extensions.FileProviders.Abstractions.dll";   DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Microsoft.Extensions.FileProviders.Physical.dll";       DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Microsoft.Extensions.FileSystemGlobbing.dll";           DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Microsoft.Extensions.Primitives.dll";                   DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceBase}\Microsoft.IO.RecyclableMemoryStream.dll";               DestDir: "{app}"; Flags: ignoreversion

; --- XAML Behaviors ---
Source: "{#SourceBase}\Microsoft.Xaml.Behaviors.dll";    DestDir: "{app}"; Flags: ignoreversion

; ============================================================
;  CODE — writes .addin manifest on install, deletes on uninstall
; ============================================================
[Code]

const
  AddinFileName = 'rf12_Revit_2026.addin';

function GetAddinFilePath(): String;
begin
  Result := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2026\' + AddinFileName);
end;

function GetDllInstallPath(): String;
begin
  Result := ExpandConstant('{commonappdata}\Autodesk\Revit\Addins\2026\{#PluginSubDir}\Revit26_Plugin_Revised.dll');
end;

procedure WriteAddinManifest();
var
  Lines: TArrayOfString;
  FilePath: String;
begin
  FilePath := GetAddinFilePath();
  SetArrayLength(Lines, 10);
  Lines[0] := '<?xml version="1.0" encoding="utf-8"?>';
  Lines[1] := '<RevitAddIns>';
  Lines[2] := '  <AddIn Type="Application">';
  Lines[3] := '    <Name>Revit26_Plugin</Name>';
  Lines[4] := '    <Assembly>' + GetDllInstallPath() + '</Assembly>';
  Lines[5] := '    <AddInId>a4e8c4c7-9f8b-4a6a-9d31-532f8c980d41</AddInId>';
  Lines[6] := '    <FullClassName>Revit26_Plugin_Revised.App</FullClassName>';
  Lines[7] := '    <VendorId>RAFI</VendorId>';
  Lines[8] := '    <VendorDescription>By Rafi</VendorDescription>';
  Lines[9] := '  </AddIn>';
  SaveStringsToFile(FilePath, Lines, False);
  SaveStringToFile(FilePath, '</RevitAddIns>', True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteAddinManifest();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  FilePath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    FilePath := GetAddinFilePath();
    if FileExists(FilePath) then
      DeleteFile(FilePath);
  end;
end;
