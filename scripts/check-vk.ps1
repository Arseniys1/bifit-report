#requires -Version 5.1
[CmdletBinding()]
param(
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
    $group = Invoke-RestMethod -Method Post -Uri "https://api.vk.ru/method/groups.getById" -Body @{
        access_token = $token
        v = "5.199"
    }
    if ($group.error) { throw "VK API error: $($group.error.error_code)" }

    $dialogs = Invoke-RestMethod -Method Post -Uri "https://api.vk.ru/method/messages.getConversations" -Body @{
        access_token = $token
        v = "5.199"
        count = 20
    }
    if ($dialogs.error) { throw "VK Messages API error: $($dialogs.error.error_code)" }

    $groupItem = if ($group.response.groups) {
        $group.response.groups | Select-Object -First 1
    } else {
        $group.response | Select-Object -First 1
    }

    [pscustomobject]@{
        TokenValid = $true
        GroupId = $groupItem.id
        GroupName = $groupItem.name
        ConversationCount = $dialogs.response.count
        Peers = @($dialogs.response.items | ForEach-Object {
            [pscustomobject]@{
                PeerId = $_.conversation.peer.id
                Type = $_.conversation.peer.type
                CanWrite = $_.conversation.can_write.allowed
            }
        })
    }
}
finally {
    [Array]::Clear($plainBytes, 0, $plainBytes.Length)
}
