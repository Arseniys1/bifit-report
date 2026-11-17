#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$ServiceName = "BifitReport.LogViewer",
    [string]$InstallPath = "C:\Program Files\BifitReport.LogViewer",
    [string]$ApplicationDataPath = "C:\ProgramData\BifitReport.LogViewer",
    [switch]$KeepFiles,
    [switch]$RemoveApplicationLogs,
    [switch]$RemoveFirewallRule
)

$ErrorActionPreference = "Stop"
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$ApplicationDataPath = [IO.Path]::GetFullPath($ApplicationDataPath)
if ($InstallPath -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "InstallPath is too broad: $InstallPath"
}
if ($ApplicationDataPath -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "ApplicationDataPath is too broad: $ApplicationDataPath"
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to delete service $ServiceName." }
}

if ($RemoveFirewallRule) {
    Get-NetFirewallRule -DisplayName "BIFIT Log Viewer ($ServiceName)" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
}
if (-not $KeepFiles -and (Test-Path -LiteralPath $InstallPath)) {
    Remove-Item -LiteralPath $InstallPath -Recurse -Force
}
if ($RemoveApplicationLogs) {
    if (Test-Path -LiteralPath $ApplicationDataPath) {
        Remove-Item -LiteralPath $ApplicationDataPath -Recurse -Force
    }
}

Write-Host "Service $ServiceName was removed."
if (-not $RemoveApplicationLogs) {
    Write-Host "Application audit logs were kept in $ApplicationDataPath."
}
