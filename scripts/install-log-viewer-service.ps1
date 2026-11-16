#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$PublishPath = "",
    [string]$InstallPath = "C:\Program Files\BifitReport.LogViewer",
    [string]$LogRoot = "C:\ProgramData\BIFIT\retail\logs",
    [string]$ListenUrl = "https://0.0.0.0:7443",
    [string]$CertificateThumbprint = "",
    [string[]]$AllowedPrincipals = @("$env:USERDOMAIN\$env:USERNAME"),
    [string]$ServiceName = "BifitReport.LogViewer",
    [switch]$AllowRawLogs,
    [switch]$OpenFirewall,
    [switch]$PreserveConfiguration
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $PublishPath) {
    $PublishPath = if (Test-Path -LiteralPath (Join-Path $repositoryRoot "BifitReport.LogViewer.exe")) {
        $repositoryRoot
    } else {
        Join-Path $repositoryRoot "artifacts\log-viewer\win-x64-selfcontained"
    }
}

$PublishPath = [IO.Path]::GetFullPath($PublishPath)
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$LogRoot = [IO.Path]::GetFullPath($LogRoot)
if ($InstallPath -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "InstallPath is too broad: $InstallPath"
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishPath "BifitReport.LogViewer.exe"))) {
    throw "Published executable not found. Run scripts\publish-log-viewer.ps1 first."
}
if (-not (Test-Path -LiteralPath $LogRoot -PathType Container)) {
    throw "Log root does not exist: $LogRoot"
}
if ($AllowedPrincipals.Count -eq 0 -or $AllowedPrincipals.Where({ [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
    throw "At least one non-empty AllowedPrincipals value is required."
}

$existingConfiguration = $null
$settingsPath = Join-Path $InstallPath "appsettings.json"
if ($PreserveConfiguration) {
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        throw "-PreserveConfiguration requires an existing appsettings.json in $InstallPath."
    }
    $existingConfiguration = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8
}
else {
    $uri = [Uri]$ListenUrl
    if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne "https" -or $uri.AbsolutePath -ne "/") {
        throw "The installation script requires an HTTPS ListenUrl without a path."
    }
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "CertificateThumbprint is required. Use a trusted server certificate from LocalMachine\My."
    }
    $normalizedThumbprint = $CertificateThumbprint -replace '[^0-9A-Fa-f]', ''
    $certificate = Get-ChildItem -LiteralPath "Cert:\LocalMachine\My\$normalizedThumbprint" -ErrorAction SilentlyContinue
    if (-not $certificate -or -not $certificate.HasPrivateKey) {
        throw "A certificate with a private key was not found in LocalMachine\My: $normalizedThumbprint"
    }
    if ($certificate.NotAfter -le (Get-Date)) { throw "The server certificate has expired." }
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
    $existingService.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
Copy-Item -Path (Join-Path $PublishPath "*") -Destination $InstallPath -Recurse -Force

if ($PreserveConfiguration) {
    $existingConfiguration | Set-Content -LiteralPath $settingsPath -Encoding UTF8
}
else {
    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $settings.Server.ListenUrl = $ListenUrl
    $settings.Server.CertificateThumbprint = $normalizedThumbprint
    $settings.Access.AllowedPrincipals = @($AllowedPrincipals)
    $settings.LogViewer.Sources[0].RootPath = $LogRoot
    $settings.LogViewer.AllowRawLogs = [bool]$AllowRawLogs
    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
}

$installedExecutable = Join-Path $InstallPath "BifitReport.LogViewer.exe"
if (-not $existingService) {
    New-Service `
        -Name $ServiceName `
        -BinaryPathName ('"{0}"' -f $installedExecutable) `
        -DisplayName "BIFIT Log Viewer" `
        -Description "Authenticated read-only web viewer for BIFIT logs." `
        -StartupType Automatic | Out-Null
}

& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/30000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to configure service recovery." }

if ($OpenFirewall -and -not $PreserveConfiguration) {
    $firewallRuleName = "BIFIT Log Viewer ($ServiceName)"
    if (-not (Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $uri.Port `
            -Program $installedExecutable `
            -Profile Domain,Private | Out-Null
    }
}

Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus("Running", [TimeSpan]::FromSeconds(30))

Write-Host "Service $ServiceName is installed and running."
Write-Host "Configuration: $settingsPath"
if (-not $PreserveConfiguration) { Write-Host "URL: $ListenUrl" }
