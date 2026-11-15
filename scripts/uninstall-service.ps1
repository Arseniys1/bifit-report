#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ServiceName = "BifitReport",
    [string]$InstallPath = "C:\Program Files\BifitReport",
    [string]$StateDirectory = "C:\ProgramData\BifitReport",
    [switch]$RemoveState,
    [switch]$KeepFiles
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

# Останавливаем и вручную запущенный агент, иначе файлы программы останутся занятыми.
$processes = Get-Process -Name "BifitReport.Agent" -ErrorAction SilentlyContinue
if ($processes) {
    if ($PSCmdlet.ShouldProcess($ServiceName, "Stop a manually started agent process")) {
        $processes | Stop-Process -Force
        Start-Sleep -Seconds 1
    }
}

if (-not $KeepFiles) {
    $resolvedInstall = [IO.Path]::GetFullPath($InstallPath)
    if ($resolvedInstall -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
        throw "Refusing to delete a broad directory: $resolvedInstall"
    }
    if (Test-Path -LiteralPath $resolvedInstall) {
        if ($PSCmdlet.ShouldProcess($resolvedInstall, "Delete the installed program files")) {
            # Нельзя удалить каталог, который является текущим: уходим из него.
            $currentLocation = (Get-Location).Path
            if ($currentLocation.StartsWith($resolvedInstall, [StringComparison]::OrdinalIgnoreCase)) {
                Set-Location -LiteralPath $env:TEMP
            }
            Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
        }
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
if (-not $KeepFiles) { Write-Host "Program files removed: $InstallPath" }
if ($RemoveState) { Write-Host "State removed: $StateDirectory" }
Write-Host "The log archive (Archive:Destination) is not deleted."
