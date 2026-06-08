$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\DeskRealm.App\DeskRealm.App.csproj"
$Dist = Join-Path $Root "dist\DeskRealm"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK introuvable. Installe le .NET 8 SDK puis relance ce script."
}

if (Test-Path $Dist) {
    Remove-Item $Dist -Recurse -Force
}

New-Item -ItemType Directory -Path $Dist -Force | Out-Null

dotnet restore $Project
dotnet publish $Project -c Release -r win-x64 --self-contained false -o $Dist

Write-Host "Build terminé : $Dist" -ForegroundColor Green
