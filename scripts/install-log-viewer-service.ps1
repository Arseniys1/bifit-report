#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$PublishPath = "",
    [string]$InstallPath = "C:\Program Files\BifitReport.LogViewer",
    [string]$ApplicationDataPath = "C:\ProgramData\BifitReport.LogViewer",
    [string]$LogRoot = "C:\ProgramData\BIFIT\retail\logs",
    [string]$ListenUrl = "https://0.0.0.0:7443",
    [string]$CertificateThumbprint = "",
    [string]$AdminUser = "admin",
    [Security.SecureString]$AdminPassword,
    [string]$ServiceName = "BifitReport.LogViewer",
    [switch]$AllowRawLogs,
    [switch]$OpenFirewall,
    [switch]$PreserveConfiguration,
    [switch]$ResetAdminPassword
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
$ApplicationDataPath = [IO.Path]::GetFullPath($ApplicationDataPath)
$LogRoot = [IO.Path]::GetFullPath($LogRoot)
if ($InstallPath -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "InstallPath is too broad: $InstallPath"
}
if ($ApplicationDataPath -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "ApplicationDataPath is too broad: $ApplicationDataPath"
}
if (-not (Test-Path -LiteralPath (Join-Path $PublishPath "BifitReport.LogViewer.exe"))) {
    throw "Published executable not found. Run scripts\publish-log-viewer.ps1 first."
}
if (-not $PreserveConfiguration -and -not (Test-Path -LiteralPath $LogRoot -PathType Container)) {
    throw "Log root does not exist: $LogRoot"
}
if ([string]::IsNullOrWhiteSpace($AdminUser) -or $AdminUser.Length -gt 100 -or $AdminUser.Contains("`r") -or $AdminUser.Contains("`n")) {
    throw "AdminUser must contain between 1 and 100 characters without line breaks."
}

$settings = $null
$settingsPath = Join-Path $InstallPath "appsettings.json"
if ($PreserveConfiguration) {
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        throw "-PreserveConfiguration requires an existing appsettings.json in $InstallPath."
    }
    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
}
else {
    $uri = $null
    if (-not [Uri]::TryCreate($ListenUrl, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne "https" -or
        $uri.AbsolutePath -ne "/" -or
        $uri.Query -or
        $uri.Fragment -or
        $uri.UserInfo) {
        throw "The installation script requires an HTTPS ListenUrl without a path."
    }
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "CertificateThumbprint is required. Use a trusted server certificate from LocalMachine\My."
    }
    $normalizedThumbprint = ($CertificateThumbprint -replace '[\s:]', '').ToUpperInvariant()
    if ($normalizedThumbprint -notmatch '^[0-9A-F]{40}$') {
        throw "CertificateThumbprint must contain exactly 40 hexadecimal characters."
    }
    $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$normalizedThumbprint" -ErrorAction SilentlyContinue
    if (-not $certificate -or -not $certificate.HasPrivateKey) {
        throw "A certificate with a private key was not found in LocalMachine\My: $normalizedThumbprint"
    }
    $now = Get-Date
    if ($certificate.NotBefore -gt $now) { throw "The server certificate is not valid yet." }
    if ($certificate.NotAfter -le $now) { throw "The server certificate has expired." }
    if (-not $certificate.Verify()) { throw "The server certificate is not trusted or its chain is invalid." }
    $enhancedKeyUsage = $certificate.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.37" } | Select-Object -First 1
    if ($enhancedKeyUsage -and -not ($enhancedKeyUsage.EnhancedKeyUsages | Where-Object Value -eq "1.3.6.1.5.5.7.3.1")) {
        throw "The certificate is not valid for Server Authentication."
    }
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne "Stopped") {
    Stop-Service -Name $ServiceName -Force
    $existingService.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
}

New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
Copy-Item -Path (Join-Path $PublishPath "*") -Destination $InstallPath -Recurse -Force

if (-not $PreserveConfiguration) {
    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $settings.Server.ListenUrl = $ListenUrl
    $settings.Server.CertificateThumbprint = $normalizedThumbprint
    $settings.LogViewer.Sources[0].RootPath = $LogRoot
    $settings.LogViewer.AllowRawLogs = [bool]$AllowRawLogs
}

$fileSink = $settings.Serilog.WriteTo | Where-Object Name -eq "File" | Select-Object -First 1
if (-not $fileSink) { throw "appsettings.json has no Serilog file sink." }
$fileSink.Args.path = Join-Path $ApplicationDataPath "logs\viewer-.log"
if (-not $settings.PSObject.Properties["Authentication"]) {
    $settings | Add-Member -NotePropertyName Authentication -NotePropertyValue ([pscustomobject]@{
        DatabasePath = ""
        DataProtectionKeysPath = ""
        SessionHours = 8
    })
}
$settings.PSObject.Properties.Remove("Access")
$identityDatabasePath = Join-Path $ApplicationDataPath "identity.db"
$settings.Authentication.DatabasePath = $identityDatabasePath
$settings.Authentication.DataProtectionKeysPath = Join-Path $ApplicationDataPath "keys"
$settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

New-Item -ItemType Directory -Force -Path $ApplicationDataPath | Out-Null
& icacls.exe $ApplicationDataPath /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to restrict the application data directory ACL." }

$installedExecutable = Join-Path $InstallPath "BifitReport.LogViewer.exe"
$shouldBootstrapUser = $ResetAdminPassword -or -not (Test-Path -LiteralPath $identityDatabasePath)
if ($shouldBootstrapUser) {
    try {
        if (-not $AdminPassword) {
            $AdminPassword = Read-Host "Password for Log Viewer user '$AdminUser'" -AsSecureString
        }

        $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminPassword)
        try {
            $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
            $bootstrapArguments = @("bootstrap-user", "--username", $AdminUser)
            if ($ResetAdminPassword) { $bootstrapArguments += "--reset-password" }
            $bootstrapOutput = $plainPassword | & $installedExecutable @bootstrapArguments 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to initialize the Log Viewer user (exit code $LASTEXITCODE): $($bootstrapOutput -join [Environment]::NewLine)"
            }
        }
        finally {
            $plainPassword = $null
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
        }
    }
    catch {
        if ($existingService) { Start-Service -Name $ServiceName -ErrorAction SilentlyContinue }
        throw
    }
}

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
