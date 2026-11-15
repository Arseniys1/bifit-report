#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [switch]$SelfContained,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $folderName = if ($SelfContained) { "win-x64-selfcontained" } else { "win-x64" }
    $OutputPath = Join-Path $repositoryRoot "artifacts\publish\$folderName"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
$versionArgs = @()
if ($Version) { $versionArgs = @("-p:Version=$Version") }

New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
dotnet publish (Join-Path $repositoryRoot "src\BifitReport.Agent\BifitReport.Agent.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained $selfContainedValue `
    --output $OutputPath `
    @versionArgs

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Отдельный exe настройщика: net48, без PowerShell и без установки рантайма на кассах.
$settingsProject = Join-Path $repositoryRoot "src\BifitReport.Settings\BifitReport.Settings.csproj"
dotnet publish $settingsProject -c Release --output $OutputPath @versionArgs
if ($LASTEXITCODE -ne 0) { throw "Settings publish failed." }

# Скрипты установки кладём рядом с приложением, чтобы каталог публикации
# можно было целиком перенести на кассу и установить службу оттуда.
$scriptsTarget = Join-Path $OutputPath "scripts"
if (Test-Path -LiteralPath $scriptsTarget) {
    Remove-Item -LiteralPath $scriptsTarget -Recurse -Force
}
Copy-Item -Path (Join-Path $repositoryRoot "scripts") -Destination $scriptsTarget -Recurse -Force

Write-Host "Published: $OutputPath"
Write-Host "Installer scripts: $scriptsTarget"
