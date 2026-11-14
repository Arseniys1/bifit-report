#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$PublishPath = "",
    [string]$InstallPath = "C:\Program Files\BifitReport",
    [string]$StateDirectory = "C:\ProgramData\BifitReport",
    [string]$LogPath = "C:\ProgramData\BIFIT\retail\logs\desktop.log",
    [string]$StoreName = "",
    [ValidateSet("File", "VK")]
    [string]$Mode = "File",
    [long[]]$PeerIds = @(),
    [string]$ServiceName = "BifitReport"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $PublishPath) { $PublishPath = Join-Path $repositoryRoot "artifacts\publish\win-x64" }

$PublishPath = [IO.Path]::GetFullPath($PublishPath)
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
$LogPath = [IO.Path]::GetFullPath($LogPath)

$sourceExecutable = Join-Path $PublishPath "BifitReport.Agent.exe"
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw "Published executable not found: $sourceExecutable. Run scripts\publish.ps1 first."
}
if ($Mode -eq "VK" -and $PeerIds.Count -eq 0) {
    throw "VK mode requires at least one -PeerIds value."
}
$expectedTokenPath = Join-Path $StateDirectory "secrets\vk-token.dat"
if ($Mode -eq "VK" -and -not (Test-Path -LiteralPath $expectedTokenPath)) {
    Write-Warning "The DPAPI token file does not exist yet: $expectedTokenPath"
}
if ($InstallPath -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "InstallPath is too broad: $InstallPath"
}
if ($StateDirectory -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "StateDirectory is too broad: $StateDirectory"
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
    $existingService.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

New-Item -ItemType Directory -Force -Path $InstallPath, $StateDirectory | Out-Null
Copy-Item -Path (Join-Path $PublishPath "*") -Destination $InstallPath -Recurse -Force

$settingsPath = Join-Path $InstallPath "appsettings.json"
$settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$settings.Agent.LogPath = $LogPath
$settings.Agent.StateDirectory = $StateDirectory
$settings.Delivery.Mode = $Mode
$settings.Reports.StoreName = $StoreName
$settings.Vk.PeerIds = @($PeerIds)
$settings.Vk.TokenFile = Join-Path $StateDirectory "secrets\vk-token.dat"
$settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

& icacls.exe $StateDirectory /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to restrict the state directory ACL." }

$installedExecutable = Join-Path $InstallPath "BifitReport.Agent.exe"
if (-not $existingService) {
    New-Service `
        -Name $ServiceName `
        -BinaryPathName ('"{0}"' -f $installedExecutable) `
        -DisplayName "BIFIT Report" `
        -Description "Read-only BIFIT log observer and report delivery agent." `
        -StartupType Automatic | Out-Null
}

Start-Service -Name $ServiceName
Write-Host "Service $ServiceName is installed and running. Delivery mode: $Mode"
Write-Host "Status: $StateDirectory\status.json"
