$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\DeskRealm.App\DeskRealm.App.csproj"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK introuvable. Installe le .NET 8 SDK puis relance ce script."
}

dotnet run --project $Project
