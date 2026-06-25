[CmdletBinding()]
param(
    [switch]$SkipClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root 'src\DeskRealm.App\DeskRealm.App.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK was not found. Install the SDK selected by global.json, then run this script again.'
}

if (-not (Test-Path -LiteralPath $Project)) {
    throw "DeskRealm project file was not found: $Project"
}

if (-not $SkipClean) {
    & (Join-Path $PSScriptRoot 'Clean-DeskRealm.ps1') -BuildArtifacts
}

# dotnet run restores when needed. Do not make normal development depend on
# candidate repair templates or source-text preflight scripts.
dotnet run --project $Project --configuration Debug
