[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v?\d+\.\d+\.\d+$')]
    [string]$Version,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$VersionNumber = $Version.TrimStart('v')
$Tag = 'v' + $VersionNumber

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required for GitHub release preparation but was not found in PATH.'
}

$GitRoot = (& git -C $Root rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0 -or -not $GitRoot) {
    throw 'Prepare-GitHubRelease.ps1 must run inside the DeskRealm Git worktree.'
}
if ((Resolve-Path -LiteralPath $GitRoot).Path -ne (Resolve-Path -LiteralPath $Root).Path) {
    throw "Git root mismatch. Expected $Root, found $GitRoot."
}

$VersionFile = (Get-Content -LiteralPath (Join-Path $Root 'VERSION.txt') -Raw -Encoding UTF8).Trim()
if ($VersionFile -ne $VersionNumber) {
    throw "VERSION.txt is $VersionFile but release request is $VersionNumber."
}

$ProjectText = Get-Content -LiteralPath (Join-Path $Root 'src\DeskRealm.App\DeskRealm.App.csproj') -Raw -Encoding UTF8
if (-not $ProjectText.Contains("<Version>$VersionNumber</Version>")) {
    throw "DeskRealm.App.csproj does not declare Version $VersionNumber."
}

$ReleaseNotes = Join-Path $Root ("docs\release-notes\v$VersionNumber.md")
if (-not (Test-Path -LiteralPath $ReleaseNotes)) {
    throw "Release notes are missing: $ReleaseNotes"
}
$ReleaseNotesText = Get-Content -LiteralPath $ReleaseNotes -Raw -Encoding UTF8
if ($ReleaseNotesText -match '(?im)^>\s*draft|not public until|validation required before publication') {
    throw "Release notes still contain draft/publication-gate wording: $ReleaseNotes"
}
$ReleaseControl = Join-Path $Root ("docs\validation\v$VersionNumber-release-control.md")
if (-not (Test-Path -LiteralPath $ReleaseControl)) {
    throw "Release-control validation is missing: $ReleaseControl"
}

$ChangelogText = Get-Content -LiteralPath (Join-Path $Root 'CHANGELOG.md') -Raw -Encoding UTF8
# Keep PowerShell sources ASCII-only for Windows PowerShell 5.1 compatibility.
# The public CHANGELOG still uses an em dash; construct that character at runtime.
$ChangelogTitleSeparator = [char]0x2014
$ChangelogHeaderPattern = '(?m)^##\s+' + [regex]::Escape($Tag) + '\s+' + [regex]::Escape([string]$ChangelogTitleSeparator) + '\s+\S'
if ($ChangelogText -notmatch $ChangelogHeaderPattern) {
    throw "CHANGELOG.md must contain a titled release header: ## $Tag [em dash] Release title."
}

if ($SkipBuild) {
    Write-Warning 'Skipping local Build-Release validation because -SkipBuild was passed. Do not use this for a normal public release.'
}
else {
    & (Join-Path $PSScriptRoot 'Build-Release.ps1')
}

Write-Host "GitHub release preparation passed for $Tag." -ForegroundColor Green
