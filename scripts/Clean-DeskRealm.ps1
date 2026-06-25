[CmdletBinding()]
param(
    [switch]$BuildArtifacts,
    [switch]$ReleaseArtifacts,
    [switch]$All
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$AppProjectDirectory = Join-Path $Root "src\DeskRealm.App"

if (-not $BuildArtifacts -and -not $ReleaseArtifacts -and -not $All) {
    $BuildArtifacts = $true
}

$Targets = New-Object System.Collections.Generic.List[string]
if ($BuildArtifacts -or $All) {
    $Targets.Add((Join-Path $AppProjectDirectory "bin"))
    $Targets.Add((Join-Path $AppProjectDirectory "obj"))
}
if ($ReleaseArtifacts -or $All) {
    $Targets.Add((Join-Path $Root "dist"))
    $Targets.Add((Join-Path $Root "artifacts"))
    $Targets.Add((Join-Path $Root ".release-work"))
}

foreach ($Target in $Targets | Select-Object -Unique) {
    if (Test-Path -LiteralPath $Target) {
        Remove-Item -LiteralPath $Target -Recurse -Force
        Write-Host "Removed generated output: $Target" -ForegroundColor DarkGray
    }
}

Write-Host "DeskRealm generated-output cleanup complete (bin/obj include XAML-generated files; source files were not touched)." -ForegroundColor Green
