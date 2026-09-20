param(
    [Parameter(Mandatory = $true)]
    [string]$AppDirectory
)
$ErrorActionPreference = 'Stop'
$appRoot = (Resolve-Path -LiteralPath $AppDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $appRoot 'HomeApp.exe'))) {
    throw 'AppDirectory must contain the built HomeApp.exe.'
}
if (Get-Process -Name HomeApp -ErrorAction SilentlyContinue) {
    throw 'Close HomeApp before registering its notification identity.'
}
$repoRoot = Split-Path $PSScriptRoot -Parent
# Keep the registered manifest in place: Windows refers to it after registration.
$identityRoot = Join-Path $appRoot 'NotificationIdentity'
New-Item -ItemType Directory -Path $identityRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'src/HomeApp/Assets') -Destination $identityRoot -Recurse -Force
$manifestPath = Join-Path $identityRoot 'AppxManifest.xml'
$installed = Get-AppxPackage -Name '878E2E7C-C9C7-4868-BAC8-F51B72AD86B8'
$identityVersion = '1.0.0.0'
if ($installed) {
    $current = [version]$installed.Version
    $identityVersion = '{0}.{1}.{2}.{3}' -f $current.Major, $current.Minor, $current.Build, ($current.Revision + 1)
}
@'
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
 xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
 xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 IgnorableNamespaces="uap uap3 uap10 desktop6 rescap">
 <Identity Name="878E2E7C-C9C7-4868-BAC8-F51B72AD86B8" Publisher="CN=AppPublisher" Version="1.0.0.0" ProcessorArchitecture="neutral" />
 <Properties>
  <DisplayName>HomeApp</DisplayName><PublisherDisplayName>AppPublisher</PublisherDisplayName>
  <Logo>Assets\StoreLogo.png</Logo><uap10:AllowExternalContent>true</uap10:AllowExternalContent>
  <desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>
 </Properties>
 <Resources><Resource Language="ja-jp" /></Resources>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22000.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Applications>
  <Application Id="App" Executable="HomeApp.exe" uap10:TrustLevel="mediumIL" uap10:RuntimeBehavior="win32App">
   <uap:VisualElements DisplayName="HomeApp" Description="HomeApp" BackgroundColor="transparent"
    Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
  </Application>
 </Applications>
 <Capabilities>
  <rescap:Capability Name="runFullTrust" />
  <rescap:Capability Name="unvirtualizedResources" />
  <uap3:Capability Name="userNotificationListener" />
 </Capabilities>
</Package>
'@ | ForEach-Object { $_.Replace('Version="1.0.0.0"', ('Version="' + $identityVersion + '"')) } |
    Set-Content -LiteralPath $manifestPath -Encoding UTF8
Add-AppxPackage -Register $manifestPath -ExternalLocation $appRoot
Get-AppxPackage -Name '878E2E7C-C9C7-4868-BAC8-F51B72AD86B8' |
    Select-Object Name, PackageFamilyName, Status
