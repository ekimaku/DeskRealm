param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\DeskRealm",
    [switch]$StartAfterInstall,
    [switch]$StartWithWindows
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appSource = Join-Path $scriptRoot 'app'
$exeName = 'DeskRealm.App.exe'
$exeSource = Join-Path $appSource $exeName

if (-not (Test-Path $exeSource)) {
    throw "DeskRealm executable not found in install bundle: $exeSource"
}

Write-Host "Installing DeskRealm to $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item (Join-Path $appSource '*') $InstallDir -Recurse -Force

$exeTarget = Join-Path $InstallDir $exeName

$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DeskRealm'
New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
$shortcutPath = Join-Path $startMenuDir 'DeskRealm.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exeTarget
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = 'DeskRealm - virtual desktop realms for Windows'
$shortcut.Save()

if ($StartWithWindows) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -Path $runKey -Name 'DeskRealm' -Value ('"{0}"' -f $exeTarget)
    Write-Host 'Startup with Windows enabled.'
}

Write-Host 'DeskRealm installed.'
Write-Host "Start menu shortcut: $shortcutPath"

if ($StartAfterInstall) {
    Start-Process -FilePath $exeTarget -WorkingDirectory $InstallDir
}
