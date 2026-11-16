<#
.SYNOPSIS
    Builds the WritingIDE installers: a self-contained publish per architecture, packed with Velopack.

.DESCRIPTION
    For each architecture (x64 and ARM64 by default) this publishes src\WritingIDE with its publish profile and runs
    "vpk pack", which writes Setup.exe, a portable zip and the update packages to the output folder (one Velopack
    channel per architecture: win-x64, win-arm64). Upload the folder's contents to the place -UpdateUrl names; installed
    copies check there for updates.

    Delta updates (small downloads) need the previous release in the output folder first, for example:
        dotnet vpk download github --repoUrl https://github.com/<owner>/<releases-repo> --channel win-x64 -o artifacts\releases

.EXAMPLE
    .\build\release.ps1 -Version 1.0.0 -UpdateUrl https://github.com/<owner>/<releases-repo>

.EXAMPLE
    .\build\release.ps1 -Version 1.0.1 -Architectures x64 -AzureTrustedSignFile .\signing\metadata.json
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    # Where installed copies look for updates: a GitHub repository with public releases, or an HTTPS folder.
    # Empty builds installers that never check for updates.
    [string] $UpdateUrl = "",

    [ValidateSet("x64", "arm64")]
    [string[]] $Architectures = @("x64", "arm64"),

    [string] $OutputDir = "artifacts\releases",

    # Markdown release notes shown by Velopack.
    [string] $ReleaseNotes = "",

    # Azure Trusted Signing metadata.json: signs WritingIDE.exe and Setup.exe so SmartScreen does not warn.
    [string] $AzureTrustedSignFile = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Velopack installs to %LocalAppData%\<packId> and deletes that folder on uninstall, so the id must differ from
# %LocalAppData%\WritingIDE, which holds the writer's extensions, fonts and crash-recovery files.
$packId = "WritingIDE.App"
$project = "src\WritingIDE\WritingIDE.csproj"
$icon = "src\WritingIDE\Assets\WritingIDE.ico"

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed." }
New-Item -ItemType Directory -Force $OutputDir | Out-Null

foreach ($architecture in $Architectures) {
    $publishProfile = if ($architecture -eq "x64") { "FolderProfile" } else { "FolderProfileArm64" }
    $publishDir = "src\WritingIDE\bin\publish\win-$architecture"
    # Stale files from an earlier publish would end up in the installer.
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    Write-Host "Publishing WritingIDE $Version for win-$architecture..." -ForegroundColor Cyan
    dotnet publish $project -p:PublishProfile=$publishProfile -p:Version=$Version -p:UpdateUrl=$UpdateUrl
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for win-$architecture." }

    Write-Host "Packing win-$architecture..." -ForegroundColor Cyan
    $pack = @(
        "vpk", "pack",
        "--packId", $packId,
        "--packVersion", $Version,
        "--packDir", $publishDir,
        "--mainExe", "WritingIDE.exe",
        "--packTitle", "WritingIDE",
        "--packAuthors", "WritingIDE",
        "--icon", $icon,
        "--channel", "win-$architecture",
        "--runtime", "win-$architecture",
        "--shortcuts", "StartMenuRoot,Desktop",
        "--outputDir", $OutputDir
    )
    if ($ReleaseNotes) { $pack += @("--releaseNotes", $ReleaseNotes) }
    if ($AzureTrustedSignFile) { $pack += @("--azureTrustedSignFile", $AzureTrustedSignFile) }
    dotnet @pack
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed for win-$architecture." }
}

Write-Host ""
Write-Host "Release files in $OutputDir :" -ForegroundColor Green
Get-ChildItem $OutputDir | Sort-Object Name | Format-Table Name, @{ Label = "Size (MB)"; Expression = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
if (-not $UpdateUrl) {
    Write-Warning "No -UpdateUrl: these installers never check for updates."
}