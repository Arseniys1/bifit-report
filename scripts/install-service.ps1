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
    [switch]$ArchiveEnabled,
    [string]$ArchiveDestination = "",
    [string]$ArchiveSourceName = "",
    [string]$ServiceName = "BifitReport",
    # Инсталлятор уже разложил файлы в каталог установки, поэтому копирование
    # и остановка службы выполняются им же (до записи файлов).
    [switch]$SkipFileCopy,
    # Обновление через инсталлятор не должно затирать настройки, заданные
    # администратором в графическом настройщике или через этот же скрипт.
    [switch]$PreserveConfiguration
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $PublishPath) {
    # При запуске из каталога публикации (scripts лежат рядом с exe) берём его,
    # иначе — стандартный каталог сборки в репозитории.
    $PublishPath = if (Test-Path -LiteralPath (Join-Path $repositoryRoot "BifitReport.Agent.exe")) {
        $repositoryRoot
    } else {
        Join-Path $repositoryRoot "artifacts\publish\win-x64"
    }
}

$PublishPath = [IO.Path]::GetFullPath($PublishPath)
$InstallPath = [IO.Path]::GetFullPath($InstallPath)
$StateDirectory = [IO.Path]::GetFullPath($StateDirectory)
$LogPath = [IO.Path]::GetFullPath($LogPath)

$sourceExecutable = Join-Path $PublishPath "BifitReport.Agent.exe"
if (-not $SkipFileCopy -and -not (Test-Path -LiteralPath $sourceExecutable)) {
    throw "Published executable not found: $sourceExecutable. Run scripts\publish.ps1 first."
}
if ($PreserveConfiguration -and -not (Test-Path -LiteralPath (Join-Path $InstallPath "appsettings.json"))) {
    throw "-PreserveConfiguration requires an existing appsettings.json in $InstallPath."
}
if (-not $PreserveConfiguration) {
    if ($Mode -eq "VK" -and $PeerIds.Count -eq 0) {
        throw "VK mode requires at least one -PeerIds value."
    }
    if ([string]::IsNullOrWhiteSpace($StoreName)) {
        Write-Warning "StoreName is empty: reports will not identify the kassa."
    }
    $expectedTokenPath = Join-Path $StateDirectory "secrets\vk-token.dat"
    if ($Mode -eq "VK" -and -not (Test-Path -LiteralPath $expectedTokenPath)) {
        Write-Warning "The DPAPI token file does not exist yet: $expectedTokenPath"
    }
}
if ($InstallPath -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "InstallPath is too broad: $InstallPath"
}
if ($StateDirectory -in @("C:\", "C:\Program Files", "C:\ProgramData")) {
    throw "StateDirectory is too broad: $StateDirectory"
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $SkipFileCopy) {
    if ($existingService -and $existingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
        $existingService.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(30))
    }

    New-Item -ItemType Directory -Force -Path $InstallPath, $StateDirectory | Out-Null
    Copy-Item -Path (Join-Path $PublishPath "*") -Destination $InstallPath -Recurse -Force
}

# Каталог состояния нужен и при установке инсталлятором: файлы уже на месте,
# а ACL ниже всё равно должен применяться к существующему каталогу.
New-Item -ItemType Directory -Force -Path $StateDirectory | Out-Null

# appsettings.local.json перекрывает appsettings.json. Если он попал в каталог
# публикации при локальных проверках, после установки он вернул бы старые
# тестовые значения (режим доставки, получатели, имя точки) вместо заданных здесь.
$localSettingsPath = Join-Path $InstallPath "appsettings.local.json"
if (Test-Path -LiteralPath $localSettingsPath) {
    Remove-Item -LiteralPath $localSettingsPath -Force
    Write-Warning "Removed stale local settings override: $localSettingsPath"
}

$settingsPath = Join-Path $InstallPath "appsettings.json"
if (-not $PreserveConfiguration) {
    $settings = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $settings.Agent.LogPath = $LogPath
    $settings.Agent.StateDirectory = $StateDirectory
    $settings.Delivery.Mode = $Mode
    $settings.Reports.StoreName = $StoreName
    $settings.Vk.PeerIds = @($PeerIds)
    $settings.Vk.TokenFile = Join-Path $StateDirectory "secrets\vk-token.dat"

    if (-not $settings.PSObject.Properties['Archive']) {
        throw "appsettings.json has no Archive section. Publish the current version of the agent first."
    }
    $settings.Archive.Enabled = [bool]$ArchiveEnabled
    if ($ArchiveEnabled) {
        if ([string]::IsNullOrWhiteSpace($ArchiveDestination)) {
            throw "-ArchiveEnabled requires -ArchiveDestination."
        }
        if ([string]::IsNullOrWhiteSpace($ArchiveSourceName)) {
            $ArchiveSourceName = if ([string]::IsNullOrWhiteSpace($StoreName)) { $env:COMPUTERNAME } else { $StoreName }
        }
        if ($ArchiveSourceName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
            throw "ArchiveSourceName contains characters that are not allowed in a directory name: $ArchiveSourceName"
        }
        $settings.Archive.Destination = $ArchiveDestination
        $settings.Archive.Sources = @([pscustomobject]@{
            Name = $ArchiveSourceName
            Path = (Split-Path -Parent $LogPath)
        })
        if ($ArchiveDestination -match '^\\\\') {
            Write-Warning "UNC archive: the service runs as LocalSystem by default. Make sure that account can write to $ArchiveDestination, or change the service logon account."
        }
    }
    else {
        $settings.Archive.Destination = ""
        $settings.Archive.Sources = @()
    }

    $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
}

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
Write-Host "Service $ServiceName is installed and running."
if ($PreserveConfiguration) {
    Write-Host "Configuration preserved: $settingsPath"
}
else {
    Write-Host "Delivery mode: $Mode"
    if ($ArchiveEnabled) {
        Write-Host "Archive: $ArchiveDestination\$ArchiveSourceName"
    }
}
Write-Host "Settings: $InstallPath\BifitReport.Settings.exe"
Write-Host "Status: $StateDirectory\status.json"
