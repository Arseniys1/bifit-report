#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $folderName = if ($SelfContained) { "win-x64-selfcontained" } else { "win-x64" }
    $OutputPath = Join-Path $repositoryRoot "artifacts\publish\$folderName"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }

New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
dotnet publish (Join-Path $repositoryRoot "src\BifitReport.Agent\BifitReport.Agent.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained $selfContainedValue `
    --output $OutputPath

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Скрипты установки кладём рядом с приложением, чтобы каталог публикации
# можно было целиком перенести на кассу и установить службу оттуда.
$scriptsTarget = Join-Path $OutputPath "scripts"
if (Test-Path -LiteralPath $scriptsTarget) {
    Remove-Item -LiteralPath $scriptsTarget -Recurse -Force
}
Copy-Item -Path (Join-Path $repositoryRoot "scripts") -Destination $scriptsTarget -Recurse -Force

Write-Host "Published: $OutputPath"
Write-Host "Installer scripts: $scriptsTarget"
