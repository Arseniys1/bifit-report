#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputPath = "",
    [switch]$FrameworkDependent,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $folder = if ($FrameworkDependent) { "win-x64" } else { "win-x64-selfcontained" }
    $OutputPath = Join-Path $repositoryRoot "artifacts\log-viewer\$folder"
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputRoot = [IO.Path]::GetPathRoot($OutputPath)
if ([string]::Equals($OutputPath.TrimEnd('\'), $outputRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals($OutputPath.TrimEnd('\'), $repositoryRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputPath is too broad: $OutputPath"
}
$versionArguments = @()
if ($Version) { $versionArguments = @("-p:Version=$Version") }

if (Test-Path -LiteralPath $OutputPath) {
    Remove-Item -LiteralPath $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
dotnet publish (Join-Path $repositoryRoot "src\BifitReport.LogViewer\BifitReport.LogViewer.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained (!$FrameworkDependent) `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $OutputPath `
    @versionArguments
if ($LASTEXITCODE -ne 0) { throw "Log Viewer publish failed." }

$scriptsTarget = Join-Path $OutputPath "scripts"
New-Item -ItemType Directory -Force -Path $scriptsTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "install-log-viewer-service.ps1") -Destination $scriptsTarget -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall-log-viewer-service.ps1") -Destination $scriptsTarget -Force

Write-Host "BIFIT Log Viewer published: $OutputPath"
