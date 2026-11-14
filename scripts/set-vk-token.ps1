#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$StateDirectory = "C:\ProgramData\BifitReport",
    [Security.SecureString]$Token
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Security
if (-not $Token) {
    $Token = Read-Host "Enter the VK community access token" -AsSecureString
}

$StateDirectory = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($StateDirectory))
$secretDirectory = Join-Path $StateDirectory "secrets"
$tokenPath = Join-Path $secretDirectory "vk-token.dat"
New-Item -ItemType Directory -Force -Path $secretDirectory | Out-Null

$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Token)
$plainBytes = $null
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($plain)) { throw "The token cannot be empty." }
    $plainBytes = [Text.Encoding]::UTF8.GetBytes($plain.Trim())
    $protectedBytes = [System.Security.Cryptography.ProtectedData]::Protect(
        $plainBytes,
        $null,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine)

    $temporaryPath = "$tokenPath.$([Guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllBytes($temporaryPath, $protectedBytes)
    Move-Item -LiteralPath $temporaryPath -Destination $tokenPath -Force
}
finally {
    if ($plainBytes) { [Array]::Clear($plainBytes, 0, $plainBytes.Length) }
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}

$currentUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls.exe $secretDirectory /inheritance:r /grant:r `
    "*S-1-5-18:(OI)(CI)F" `
    "*S-1-5-32-544:(OI)(CI)F" `
    "*$($currentUserSid):(OI)(CI)F" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to restrict the secrets directory ACL." }

Write-Host "The encrypted token was saved to: $tokenPath"
Write-Host "Restart BifitReport after switching Delivery:Mode to VK."
