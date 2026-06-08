param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\DeskRealm",
    [switch]$RemoveUserConfig
)

$ErrorActionPreference = 'Stop'

Write-Host 'Stopping DeskRealm if it is running...'
Get-Process -Name 'DeskRealm.App' -ErrorAction SilentlyContinue | Stop-Process -Force

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path $runKey) {
    Remove-ItemProperty -Path $runKey -Name 'DeskRealm' -ErrorAction SilentlyContinue
}

$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\DeskRealm'
if (Test-Path $startMenuDir) {
    Remove-Item $startMenuDir -Recurse -Force
}

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}

if ($RemoveUserConfig) {
    $roaming = Join-Path $env:APPDATA 'DeskRealm'
    $local = Join-Path $env:LOCALAPPDATA 'DeskRealm'
    if (Test-Path $roaming) { Remove-Item $roaming -Recurse -Force }
    if (Test-Path $local) { Remove-Item $local -Recurse -Force }
    Write-Host 'User config and logs removed.'
} else {
    Write-Host 'User config and logs preserved.'
}

Write-Host 'DeskRealm uninstalled.'
