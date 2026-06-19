$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\DeskRealm.App\DeskRealm.App.csproj"
$Dist = Join-Path $Root "dist\DeskRealm"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK introuvable. Installe le .NET 10 SDK puis relance ce script."
}

if (Test-Path $Dist) {
    Remove-Item $Dist -Recurse -Force
}

New-Item -ItemType Directory -Path $Dist -Force | Out-Null

dotnet restore $Project
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $Dist `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishTrimmed=false

Copy-Item (Join-Path $Root "LICENSE") $Dist -Force
Copy-Item (Join-Path $Root "NOTICE") $Dist -Force
Copy-Item (Join-Path $Root "README.md") $Dist -Force
Copy-Item (Join-Path $Root "VERSION.txt") $Dist -Force
Copy-Item (Join-Path $Root "scripts\Restore-Desktop.ps1") $Dist -Force

Write-Host "Build terminé : $Dist" -ForegroundColor Green
