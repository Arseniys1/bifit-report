#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [long]$PeerId,
    [string]$StateDirectory = "C:\ProgramData\BifitReport"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Security

$StateDirectory = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($StateDirectory))
$tokenPath = Join-Path $StateDirectory "secrets\vk-token.dat"
if (-not (Test-Path -LiteralPath $tokenPath)) {
    throw "VK token file not found: $tokenPath"
}

$protectedBytes = [IO.File]::ReadAllBytes($tokenPath)
$plainBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
    $protectedBytes,
    $null,
    [System.Security.Cryptography.DataProtectionScope]::LocalMachine)

try {
    $token = [Text.Encoding]::UTF8.GetString($plainBytes)
    $result = Invoke-RestMethod -Method Post -Uri "https://api.vk.ru/method/messages.send" -Body @{
        access_token = $token
        v = "5.199"
        peer_id = $PeerId
        random_id = Get-Random -Minimum 1 -Maximum ([int]::MaxValue)
        message = "BIFIT Report: test message. VK delivery is configured correctly."
    }
    if ($result.error) { throw "VK Messages API error: $($result.error.error_code)" }
    Write-Host "Test message sent. VK message id: $($result.response)"
}
finally {
    [Array]::Clear($plainBytes, 0, $plainBytes.Length)
}
