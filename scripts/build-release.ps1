#requires -Version 5.1
<#
.SYNOPSIS
    Собирает релизные артефакты BIFIT Report: инсталлятор, portable-архив и контрольные суммы.

.DESCRIPTION
    Используется локально и в GitHub Actions (.github/workflows/release.yml).
    Требует .NET 8 SDK и Inno Setup 6 (ISCC.exe).

.EXAMPLE
    .\scripts\build-release.ps1 -Version 1.0.0

.EXAMPLE
    .\scripts\build-release.ps1 -Version 1.0.0 -SkipTests
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    # Каталог для готовых файлов. По умолчанию dist\ в корне репозитория.
    [string]$OutputPath = "",

    # Пропустить dotnet test (например, когда тесты уже прошли в отдельной job).
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repositoryRoot "artifacts\release\win-x64-selfcontained"
$installerScript = Join-Path $repositoryRoot "installer\BifitReport.iss"
$distDir = if ($OutputPath) { [IO.Path]::GetFullPath($OutputPath) } else { Join-Path $repositoryRoot "dist" }

if (-not (Test-Path -LiteralPath $installerScript)) {
    throw "Installer script not found: $installerScript"
}

Write-Host "BIFIT Report release build $Version" -ForegroundColor Cyan
Write-Host "  publish:   $publishDir"
Write-Host "  artifacts: $distDir"

if (-not $SkipTests) {
    Write-Host "`n== Tests ==" -ForegroundColor Cyan
    dotnet test (Join-Path $repositoryRoot "BifitReport.sln") -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }
}

Write-Host "`n== Publish (self-contained win-x64) ==" -ForegroundColor Cyan
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
& (Join-Path $repositoryRoot "scripts\publish.ps1") -SelfContained -OutputPath $publishDir -Version $Version

# В публикацию не должны попасть локальные тестовые настройки, отладочные символы
# и секреты: архив скачивают с GitHub, он не должен ничего раскрывать.
$unwanted = @("appsettings.local.json", "*.pdb")
foreach ($pattern in $unwanted) {
    Get-ChildItem -LiteralPath $publishDir -Filter $pattern -File -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Force
}
$secretsDir = Join-Path $publishDir "secrets"
if (Test-Path -LiteralPath $secretsDir) {
    Remove-Item -LiteralPath $secretsDir -Recurse -Force
}

$requiredFiles = @("BifitReport.Agent.exe", "BifitReport.Settings.exe", "appsettings.json", "scripts\install-service.ps1")
foreach ($relative in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $relative))) {
        throw "Publish result is incomplete: $relative is missing."
    }
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
Get-ChildItem -LiteralPath $distDir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^(BifitReport-.*\.(exe|zip)|SHA256SUMS\.txt)$' } |
    Remove-Item -Force

Write-Host "`n== Portable archive ==" -ForegroundColor Cyan
$zipPath = Join-Path $distDir "BifitReport-$Version-portable-win-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDir,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

Write-Host "`n== Installer (Inno Setup) ==" -ForegroundColor Cyan
$isccCandidates = @()
if ($env:ISCC_PATH) { $isccCandidates += $env:ISCC_PATH }
$isccCandidates += @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
)
foreach ($uninstallKey in @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
    "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
)) {
    $entry = Get-ItemProperty -Path $uninstallKey -ErrorAction SilentlyContinue
    if ($entry -and $entry.InstallLocation) {
        $isccCandidates += (Join-Path $entry.InstallLocation "ISCC.exe")
    }
}
$iscc = $isccCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe (Inno Setup 6) not found. Install it with: winget install JRSoftware.InnoSetup"
}

& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$distDir" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

$installerPath = Join-Path $distDir "BifitReport-$Version-setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not created: $installerPath"
}

Write-Host "`n== SHA256 ==" -ForegroundColor Cyan
$releaseFiles = @($installerPath, $zipPath) | ForEach-Object { Get-Item -LiteralPath $_ }
$checksumLines = $releaseFiles | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
Set-Content -LiteralPath (Join-Path $distDir "SHA256SUMS.txt") -Value $checksumLines -Encoding ASCII

Write-Host "`nRelease artifacts:" -ForegroundColor Green
$releaseFiles | ForEach-Object { Write-Host ("  {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
Write-Host ("  {0}" -f (Join-Path $distDir "SHA256SUMS.txt"))
