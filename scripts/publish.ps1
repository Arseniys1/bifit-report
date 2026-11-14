#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot "artifacts\publish\win-x64"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
dotnet publish (Join-Path $repositoryRoot "src\BifitReport.Agent\BifitReport.Agent.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    --output $OutputPath

if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Write-Host "Published: $OutputPath"
