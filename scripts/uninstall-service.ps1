#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ServiceName = "BifitReport",
    [string]$StateDirectory = "C:\ProgramData\BifitReport",
    [switch]$RemoveState
)

$ErrorActionPreference = "Stop"
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }
    if ($PSCmdlet.ShouldProcess($ServiceName, "Delete the Windows service")) {
        & sc.exe delete $ServiceName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to delete service $ServiceName." }
    }
}

if ($RemoveState) {
    $resolvedState = [IO.Path]::GetFullPath($StateDirectory)
    if ($resolvedState -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
        throw "Refusing to delete a broad directory: $resolvedState"
    }
    if (Test-Path -LiteralPath $resolvedState) {
        if ($PSCmdlet.ShouldProcess($resolvedState, "Permanently delete state, queue, and DPAPI token")) {
            Remove-Item -LiteralPath $resolvedState -Recurse -Force
        }
    }
}

Write-Host "Service $ServiceName was removed."
