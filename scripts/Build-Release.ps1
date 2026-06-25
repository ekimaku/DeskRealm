[CmdletBinding()]
param(
    [switch]$SkipClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root 'src\DeskRealm.App\DeskRealm.App.csproj'
$Dist = Join-Path $Root 'dist\DeskRealm'
$Runtime = 'win-x64'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK was not found. Install the SDK selected by global.json, then run this script again.'
}

if (-not (Test-Path -LiteralPath $Project)) {
    throw "DeskRealm project file was not found: $Project"
}

if (-not $SkipClean) {
    & (Join-Path $PSScriptRoot 'Clean-DeskRealm.ps1') -All
}

# Keep the release path conventional and inspectable: restore the publish RID,
# compile Release, then publish the same project without a second restore.
dotnet restore $Project --runtime $Runtime
dotnet build $Project --configuration Release --no-restore
dotnet publish $Project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $Dist `
    --no-restore `
    /p:PublishSingleFile=true `
    /p:WindowsAppSDKSelfContained=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishTrimmed=false

$RequiredFiles = @(
    (Join-Path $Dist 'DeskRealm.App.exe'),
    (Join-Path $Root 'VERSION.txt')
)
foreach ($RequiredFile in $RequiredFiles) {
    if (-not (Test-Path -LiteralPath $RequiredFile)) {
        throw "Release build validation failed. Required file is missing: $RequiredFile"
    }
}

foreach ($FileName in @('LICENSE', 'NOTICE', 'README.md', 'VERSION.txt')) {
    Copy-Item (Join-Path $Root $FileName) $Dist -Force
}
Copy-Item (Join-Path $PSScriptRoot 'Restore-Desktop.ps1') $Dist -Force

Write-Host "DeskRealm release build completed: $Dist" -ForegroundColor Green
