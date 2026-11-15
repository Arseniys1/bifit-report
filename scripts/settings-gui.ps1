#requires -Version 5.1
<#
    Графический настройщик BifitReport.
    Запускается на кассе, редактирует appsettings.json установленной службы,
    сохраняет токен VK через DPAPI (LocalMachine) и перезапускает службу.
#>
[CmdletBinding()]
param(
    [string]$ConfigPath = "C:\Program Files\BifitReport\appsettings.json",
    [string]$Tab = "",
    [switch]$SelfTest,
    [switch]$NoElevate
)

$ErrorActionPreference = "Stop"
try { Add-Type -AssemblyName System.Security } catch { }
try { Add-Type -AssemblyName System.Security.Cryptography.ProtectedData } catch { }
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-ConfigPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "Не задан путь к appsettings.json." }
    return [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Path))
}

function Read-AgentConfig([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Файл настроек не найден:`n$Path`n`nСначала установите службу: scripts\install-service.ps1"
    }
    $config = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($section in 'Agent', 'Delivery', 'Vk', 'Reports', 'Archive') {
        if (-not $config.PSObject.Properties[$section]) {
            $config | Add-Member -NotePropertyName $section -NotePropertyValue ([pscustomobject]@{})
        }
    }
    return $config
}

function Save-AgentConfig($Config, [string]$Path) {
    $json = $Config | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
}

function Get-TokenPath($Config) {
    $raw = [string]$Config.Vk.TokenFile
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $raw = Join-Path ([string]$Config.Agent.StateDirectory) "secrets\vk-token.dat"
    }
    return [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($raw))
}

function Get-TokenStatus($Config) {
    try {
        $path = Get-TokenPath $Config
        if (-not (Test-Path -LiteralPath $path)) { return "не сохранён" }
        $protected = [IO.File]::ReadAllBytes($path)
        $plain = [Security.Cryptography.ProtectedData]::Unprotect(
            $protected, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
        try {
            $token = [Text.Encoding]::UTF8.GetString($plain)
            if ([string]::IsNullOrWhiteSpace($token)) { return "файл пуст" }
            return "сохранён"
        }
        finally { [Array]::Clear($plain, 0, $plain.Length) }
    }
    catch { return "не читается: $($_.Exception.Message)" }
}

function Read-Token($Config) {
    $path = Get-TokenPath $Config
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Токен VK не сохранён. Нажмите «Сохранить токен…»."
    }
    $protected = [IO.File]::ReadAllBytes($path)
    $plain = [Security.Cryptography.ProtectedData]::Unprotect(
        $protected, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    try { return [Text.Encoding]::UTF8.GetString($plain).Trim() }
    finally { [Array]::Clear($plain, 0, $plain.Length) }
}

function Save-Token($Config, [string]$Token) {
    if ([string]::IsNullOrWhiteSpace($Token)) { throw "Токен не может быть пустым." }
    $path = Get-TokenPath $Config
    $directory = Split-Path -Parent $path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $bytes = [Text.Encoding]::UTF8.GetBytes($Token.Trim())
    try {
        $protected = [Security.Cryptography.ProtectedData]::Protect(
            $bytes, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
        [IO.File]::WriteAllBytes($path, $protected)
    }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }

    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls.exe $directory /inheritance:r /grant:r `
        "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*${sid}:(OI)(CI)F" | Out-Null
}

function Invoke-VkApi($Config, [string]$Method, [hashtable]$Parameters) {
    $token = Read-Token $Config
    $body = @{ access_token = $token; v = '5.199' }
    foreach ($key in $Parameters.Keys) { $body[$key] = $Parameters[$key] }
    $response = Invoke-RestMethod -Method Post -Uri "https://api.vk.ru/method/$Method" -Body $body
    if ($response.error) {
        throw "VK API $($response.error.error_code): $($response.error.error_msg)"
    }
    return $response.response
}

function Format-Sources($Sources) {
    $lines = @()
    foreach ($source in @($Sources)) {
        if ($null -eq $source) { continue }
        if (-not [string]::IsNullOrWhiteSpace([string]$source.Name)) {
            $lines += ("{0}|{1}" -f $source.Name, $source.Path)
        }
    }
    return ($lines -join "`r`n")
}

function Get-ServiceLabel {
    $service = Get-Service -Name 'BifitReport' -ErrorAction SilentlyContinue
    if (-not $service) { return "не установлена" }
    return [string]$service.Status
}

function Show-TokenDialog($Owner) {
    $dialog = New-Object System.Windows.Forms.Form
    $dialog.Text = "Токен сообщества VK"
    $dialog.Size = New-Object System.Drawing.Size(520, 190)
    $dialog.StartPosition = 'CenterParent'
    $dialog.FormBorderStyle = 'FixedDialog'
    $dialog.MaximizeBox = $false
    $dialog.MinimizeBox = $false
    $dialog.Font = New-Object System.Drawing.Font('Segoe UI', 9)

    $label = New-Object System.Windows.Forms.Label
    $label.Text = "Вставьте токен сообщества (право messages):"
    $label.Location = New-Object System.Drawing.Point(12, 12)
    $label.Size = New-Object System.Drawing.Size(480, 20)
    $dialog.Controls.Add($label)

    $textBox = New-Object System.Windows.Forms.TextBox
    $textBox.Location = New-Object System.Drawing.Point(12, 36)
    $textBox.Size = New-Object System.Drawing.Size(480, 24)
    $textBox.UseSystemPasswordChar = $true
    $dialog.Controls.Add($textBox)

    $hint = New-Object System.Windows.Forms.Label
    $hint.Text = "Токен шифруется DPAPI и сохраняется только на этом компьютере."
    $hint.Location = New-Object System.Drawing.Point(12, 66)
    $hint.Size = New-Object System.Drawing.Size(480, 20)
    $hint.ForeColor = [System.Drawing.Color]::DimGray
    $dialog.Controls.Add($hint)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = "Сохранить"
    $ok.Location = New-Object System.Drawing.Point(300, 104)
    $ok.Size = New-Object System.Drawing.Size(92, 30)
    $ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $dialog.Controls.Add($ok)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = "Отмена"
    $cancel.Location = New-Object System.Drawing.Point(400, 104)
    $cancel.Size = New-Object System.Drawing.Size(92, 30)
    $cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
    $dialog.Controls.Add($cancel)

    $dialog.AcceptButton = $ok
    $dialog.CancelButton = $cancel
    $dialog.Add_Shown({ $textBox.Focus() }.GetNewClosure())
    try {
        if ($dialog.ShowDialog($Owner) -eq [System.Windows.Forms.DialogResult]::OK) {
            return $textBox.Text
        }
        return $null
    }
    finally { $dialog.Dispose() }
}

function Show-SettingsGui($Config, [string]$Path, [string]$InitialTab = "") {
    [void][System.Windows.Forms.Application]::EnableVisualStyles()

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "BifitReport — настройки"
    $form.Size = New-Object System.Drawing.Size(860, 680)
    $form.StartPosition = 'CenterScreen'
    $form.MinimumSize = New-Object System.Drawing.Size(820, 620)
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

    $tabs = New-Object System.Windows.Forms.TabControl
    $tabs.Location = New-Object System.Drawing.Point(10, 10)
    $tabs.Size = New-Object System.Drawing.Size(824, 620)
    $tabs.Anchor = 'Top,Left,Right,Bottom'
    $form.Controls.Add($tabs)

    function Add-Label($Parent, [string]$Text, [int]$X, [int]$Y, [int]$Width = 700) {
        $label = New-Object System.Windows.Forms.Label
        $label.Text = $Text
        $label.Location = New-Object System.Drawing.Point($X, $Y)
        $label.Size = New-Object System.Drawing.Size($Width, 20)
        $Parent.Controls.Add($label)
        return $label
    }

    function Add-TextBox($Parent, [string]$Text, [int]$X, [int]$Y, [int]$Width = 780) {
        $box = New-Object System.Windows.Forms.TextBox
        $box.Text = $Text
        $box.Location = New-Object System.Drawing.Point($X, $Y)
        $box.Size = New-Object System.Drawing.Size($Width, 24)
        $Parent.Controls.Add($box)
        return $box
    }

    function Add-CheckBox($Parent, [string]$Text, [bool]$Checked, [int]$X, [int]$Y, [int]$Width = 500) {
        $box = New-Object System.Windows.Forms.CheckBox
        $box.Text = $Text
        $box.Checked = $Checked
        $box.Location = New-Object System.Drawing.Point($X, $Y)
        $box.Size = New-Object System.Drawing.Size($Width, 24)
        $Parent.Controls.Add($box)
        return $box
    }

    function Add-Number($Parent, [string]$Text, [int]$Value, [int]$Min, [int]$Max, [int]$X, [int]$Y) {
        $label = Add-Label $Parent $Text $X ($Y - 1) 220
        $number = New-Object System.Windows.Forms.NumericUpDown
        $number.Minimum = $Min
        $number.Maximum = $Max
        $number.Value = [Math]::Max($Min, [Math]::Min($Max, $Value))
        $number.Location = New-Object System.Drawing.Point(($X + 230), ($Y - 3))
        $number.Size = New-Object System.Drawing.Size(90, 24)
        $Parent.Controls.Add($number)
        return $number
    }

    # ---------------- Основное ----------------
    $tabMain = New-Object System.Windows.Forms.TabPage
    $tabMain.Text = "Основное"
    $tabMain.Padding = New-Object System.Windows.Forms.Padding(10)
    $tabs.TabPages.Add($tabMain)

    Add-Label $tabMain "Путь к журналу БИФИТ (desktop.log):" 10 15 | Out-Null
    $txtLogPath = Add-TextBox $tabMain ([string]$Config.Agent.LogPath) 10 35

    Add-Label $tabMain "Каталог состояния (очередь, токен, статус):" 10 70 | Out-Null
    $txtState = Add-TextBox $tabMain ([string]$Config.Agent.StateDirectory) 10 90
    $txtState.ReadOnly = $true

    Add-Label $tabMain "Название точки (попадает в сообщение как «Точка»):" 10 125 480 | Out-Null
    $txtStore = Add-TextBox $tabMain ([string]$Config.Reports.StoreName) 10 145 480

    $lblMode = New-Object System.Windows.Forms.Label
    $lblMode.Text = "Режим:"
    $lblMode.Location = New-Object System.Drawing.Point(520, 125)
    $lblMode.Size = New-Object System.Drawing.Size(60, 20)
    $tabMain.Controls.Add($lblMode)

    $cmbMode = New-Object System.Windows.Forms.ComboBox
    $cmbMode.DropDownStyle = 'DropDownList'
    $cmbMode.Items.AddRange(@('VK', 'File'))
    $cmbMode.Location = New-Object System.Drawing.Point(585, 145)
    $cmbMode.Size = New-Object System.Drawing.Size(100, 24)
    $cmbMode.SelectedItem = if ([string]$Config.Delivery.Mode -eq 'VK') { 'VK' } else { 'File' }
    $tabMain.Controls.Add($cmbMode)

    $lblService = Add-Label $tabMain ("Служба BifitReport: " + (Get-ServiceLabel)) 10 185

    $btnSave = New-Object System.Windows.Forms.Button
    $btnSave.Text = "Сохранить"
    $btnSave.Location = New-Object System.Drawing.Point(10, 215)
    $btnSave.Size = New-Object System.Drawing.Size(130, 32)
    $tabMain.Controls.Add($btnSave)

    $btnSaveRestart = New-Object System.Windows.Forms.Button
    $btnSaveRestart.Text = "Сохранить и перезапустить"
    $btnSaveRestart.Location = New-Object System.Drawing.Point(150, 215)
    $btnSaveRestart.Size = New-Object System.Drawing.Size(210, 32)
    $tabMain.Controls.Add($btnSaveRestart)

    $btnOpenState = New-Object System.Windows.Forms.Button
    $btnOpenState.Text = "Открыть каталог состояния"
    $btnOpenState.Location = New-Object System.Drawing.Point(370, 215)
    $btnOpenState.Size = New-Object System.Drawing.Size(200, 32)
    $tabMain.Controls.Add($btnOpenState)

    $hintMain = New-Object System.Windows.Forms.Label
    $hintMain.Text = "Настройки сохраняются в appsettings.json службы. Для сохранения нужны права администратора.`r`n" +
        "После изменения пути журнала или получателей перезапустите службу."
    $hintMain.Location = New-Object System.Drawing.Point(10, 265)
    $hintMain.Size = New-Object System.Drawing.Size(790, 60)
    $hintMain.ForeColor = [System.Drawing.Color]::DimGray
    $tabMain.Controls.Add($hintMain)

    # ---------------- VK ----------------
    $tabVk = New-Object System.Windows.Forms.TabPage
    $tabVk.Text = "VK"
    $tabVk.Padding = New-Object System.Windows.Forms.Padding(10)
    $tabs.TabPages.Add($tabVk)

    Add-Label $tabVk "Получатели (peer_id через запятую; чат = 2000000000 + chat_id):" 10 15 | Out-Null
    $txtPeers = Add-TextBox $tabVk ((@($Config.Vk.PeerIds) | Where-Object { $_ }) -join ', ') 10 35

    $lblToken = Add-Label $tabVk ("Токен VK: " + (Get-TokenStatus $Config)) 10 70

    $btnToken = New-Object System.Windows.Forms.Button
    $btnToken.Text = "Сохранить токен…"
    $btnToken.Location = New-Object System.Drawing.Point(10, 95)
    $btnToken.Size = New-Object System.Drawing.Size(160, 30)
    $tabVk.Controls.Add($btnToken)

    $btnCheckVk = New-Object System.Windows.Forms.Button
    $btnCheckVk.Text = "Проверить VK"
    $btnCheckVk.Location = New-Object System.Drawing.Point(180, 95)
    $btnCheckVk.Size = New-Object System.Drawing.Size(130, 30)
    $tabVk.Controls.Add($btnCheckVk)

    $btnSendTest = New-Object System.Windows.Forms.Button
    $btnSendTest.Text = "Отправить тест"
    $btnSendTest.Location = New-Object System.Drawing.Point(320, 95)
    $btnSendTest.Size = New-Object System.Drawing.Size(150, 30)
    $tabVk.Controls.Add($btnSendTest)

    $outputVk = New-Object System.Windows.Forms.TextBox
    $outputVk.Multiline = $true
    $outputVk.ReadOnly = $true
    $outputVk.ScrollBars = 'Vertical'
    $outputVk.Location = New-Object System.Drawing.Point(10, 140)
    $outputVk.Size = New-Object System.Drawing.Size(790, 430)
    $outputVk.Anchor = 'Top,Left,Right,Bottom'
    $outputVk.Font = New-Object System.Drawing.Font('Consolas', 9)
    $tabVk.Controls.Add($outputVk)

    # ---------------- Отчёты ----------------
    $tabReports = New-Object System.Windows.Forms.TabPage
    $tabReports.Text = "Отчёты"
    $tabReports.Padding = New-Object System.Windows.Forms.Padding(10)
    $tabs.TabPages.Add($tabReports)

    $chkShiftOpen = Add-CheckBox $tabReports "Сообщать об открытии смены" ([bool]$Config.Reports.SendShiftOpened) 10 15
    $chkShiftClose = Add-CheckBox $tabReports "Отправлять итог при закрытии смены" ([bool]$Config.Reports.SendShiftClosed) 10 45
    $chkCritical = Add-CheckBox $tabReports "Немедленные аварийные уведомления" ([bool]$Config.Reports.SendImmediateCriticalErrors) 10 75
    $chkErrorText = Add-CheckBox $tabReports "Добавлять очищенный текст ошибки" ([bool]$Config.Reports.IncludeSanitizedErrorMessage) 10 105
    $chkDetails = Add-CheckBox $tabReports "Добавлять фрагмент стека/исключения" ([bool]$Config.Reports.IncludeExceptionDetails) 10 135
    $chkPeriodic = Add-CheckBox $tabReports "Периодические промежуточные отчёты" ([bool]$Config.Reports.PeriodicSummaryEnabled) 10 165

    $numCooldown = Add-Number $tabReports "Повтор алерта, мин:" ([int]$Config.Reports.CriticalErrorCooldownMinutes) 1 1440 10 210
    $numPeriodic = Add-Number $tabReports "Период сводки, мин:" ([int]$Config.Reports.PeriodicSummaryMinutes) 5 1440 10 250
    $numLines = Add-Number $tabReports "Строк стека в алерте:" ([int]$Config.Reports.MaxExceptionLines) 0 50 10 290
    $numChars = Add-Number $tabReports "Символов стека в алерте:" ([int]$Config.Reports.MaxExceptionCharacters) 100 3000 10 330

    $hintReports = New-Object System.Windows.Forms.Label
    $hintReports.Text = "Фрагмент стека очищается от токенов, телефонов, карт и e-mail.`r`n" +
        "Итоговое сообщение остаётся в пределах ~1,5 КБ при лимите VK 9000 символов."
    $hintReports.Location = New-Object System.Drawing.Point(10, 380)
    $hintReports.Size = New-Object System.Drawing.Size(780, 50)
    $hintReports.ForeColor = [System.Drawing.Color]::DimGray
    $tabReports.Controls.Add($hintReports)

    # ---------------- Архив ----------------
    $tabArchive = New-Object System.Windows.Forms.TabPage
    $tabArchive.Text = "Архив"
    $tabArchive.Padding = New-Object System.Windows.Forms.Padding(10)
    $tabs.TabPages.Add($tabArchive)

    $chkArchive = Add-CheckBox $tabArchive "Копировать ротированные журналы в архив (до удаления в БИФИТ)" ([bool]$Config.Archive.Enabled) 10 15 780

    Add-Label $tabArchive "Каталог архива (лучше другой диск или сетевой ресурс):" 10 50 | Out-Null
    $txtArchiveDest = Add-TextBox $tabArchive ([string]$Config.Archive.Destination) 10 70

    Add-Label $tabArchive "Источники: по одному в строке, формат Имя|Путь" 10 105 | Out-Null
    $txtSources = New-Object System.Windows.Forms.TextBox
    $txtSources.Multiline = $true
    $txtSources.ScrollBars = 'Vertical'
    $txtSources.Text = Format-Sources $Config.Archive.Sources
    $txtSources.Location = New-Object System.Drawing.Point(10, 125)
    $txtSources.Size = New-Object System.Drawing.Size(790, 120)
    $txtSources.Anchor = 'Top,Left,Right'
    $txtSources.Font = New-Object System.Drawing.Font('Consolas', 9)
    $tabArchive.Controls.Add($txtSources)

    $numPoll = Add-Number $tabArchive "Период проверки, мин:" ([int]$Config.Archive.PollIntervalMinutes) 1 1440 10 265
    $numAge = Add-Number $tabArchive "Не копировать файлы моложе, мин:" ([int]$Config.Archive.MinFileAgeMinutes) 0 1440 10 305

    $hintArchive = New-Object System.Windows.Forms.Label
    $hintArchive.Text = "Источник только читается, архив ничего не удаляет.`r`n" +
        "Служба работает от LocalSystem: для сетевого каталога этому аккаунту нужен доступ."
    $hintArchive.Location = New-Object System.Drawing.Point(10, 350)
    $hintArchive.Size = New-Object System.Drawing.Size(780, 50)
    $hintArchive.ForeColor = [System.Drawing.Color]::DimGray
    $tabArchive.Controls.Add($hintArchive)

    # ---------------- Диагностика ----------------
    $tabDiagnostics = New-Object System.Windows.Forms.TabPage
    $tabDiagnostics.Text = "Диагностика"
    $tabDiagnostics.Padding = New-Object System.Windows.Forms.Padding(10)
    $tabs.TabPages.Add($tabDiagnostics)

    $btnRefresh = New-Object System.Windows.Forms.Button
    $btnRefresh.Text = "Обновить"
    $btnRefresh.Location = New-Object System.Drawing.Point(10, 10)
    $btnRefresh.Size = New-Object System.Drawing.Size(120, 30)
    $tabDiagnostics.Controls.Add($btnRefresh)

    $outputDiag = New-Object System.Windows.Forms.TextBox
    $outputDiag.Multiline = $true
    $outputDiag.ReadOnly = $true
    $outputDiag.ScrollBars = 'Both'
    $outputDiag.WordWrap = $false
    $outputDiag.Location = New-Object System.Drawing.Point(10, 50)
    $outputDiag.Size = New-Object System.Drawing.Size(790, 520)
    $outputDiag.Anchor = 'Top,Left,Right,Bottom'
    $outputDiag.Font = New-Object System.Drawing.Font('Consolas', 9)
    $tabDiagnostics.Controls.Add($outputDiag)

    # ---------------- Действия ----------------
    $readPeers = {
        $peers = @()
        foreach ($part in ($txtPeers.Text -split '[,;\s]+')) {
            if ([string]::IsNullOrWhiteSpace($part)) { continue }
            $value = 0L
            if (-not [long]::TryParse($part.Trim(), [ref]$value) -or $value -le 0) {
                throw "Некорректный peer_id: $part"
            }
            $peers += $value
        }
        return $peers
    }.GetNewClosure()

    $applyToConfig = {
        if ([string]::IsNullOrWhiteSpace($txtLogPath.Text)) { throw "Путь к журналу не задан." }
        if ([string]::IsNullOrWhiteSpace($txtState.Text)) { throw "Каталог состояния не задан." }

        $peers = & $readPeers
        if ($cmbMode.SelectedItem -eq 'VK' -and $peers.Count -eq 0) {
            throw "Для режима VK нужен хотя бы один peer_id."
        }

        $Config.Agent.LogPath = $txtLogPath.Text.Trim()
        $Config.Agent.StateDirectory = $txtState.Text.Trim()
        $Config.Reports.StoreName = $txtStore.Text.Trim()
        $Config.Delivery.Mode = [string]$cmbMode.SelectedItem
        $Config.Vk.PeerIds = @($peers)

        $Config.Reports.SendShiftOpened = [bool]$chkShiftOpen.Checked
        $Config.Reports.SendShiftClosed = [bool]$chkShiftClose.Checked
        $Config.Reports.SendImmediateCriticalErrors = [bool]$chkCritical.Checked
        $Config.Reports.IncludeSanitizedErrorMessage = [bool]$chkErrorText.Checked
        $Config.Reports.IncludeExceptionDetails = [bool]$chkDetails.Checked
        $Config.Reports.PeriodicSummaryEnabled = [bool]$chkPeriodic.Checked
        $Config.Reports.CriticalErrorCooldownMinutes = [int]$numCooldown.Value
        $Config.Reports.PeriodicSummaryMinutes = [int]$numPeriodic.Value
        $Config.Reports.MaxExceptionLines = [int]$numLines.Value
        $Config.Reports.MaxExceptionCharacters = [int]$numChars.Value

        $Config.Archive.Enabled = [bool]$chkArchive.Checked
        $Config.Archive.Destination = $txtArchiveDest.Text.Trim()
        $Config.Archive.PollIntervalMinutes = [int]$numPoll.Value
        $Config.Archive.MinFileAgeMinutes = [int]$numAge.Value

        $sources = @()
        foreach ($line in ($txtSources.Text -split "`r?`n")) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $parts = $line -split '\|', 2
            if ($parts.Count -ne 2 -or [string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1])) {
                throw "Источник должен быть в формате Имя|Путь: $line"
            }
            $sources += [pscustomobject]@{
                Name = $parts[0].Trim()
                Path = $parts[1].Trim()
            }
        }
        if ($Config.Archive.Enabled -and $sources.Count -eq 0) {
            throw "Для архива добавьте хотя бы один источник."
        }
        if ($Config.Archive.Enabled -and [string]::IsNullOrWhiteSpace($Config.Archive.Destination)) {
            throw "Для архива укажите каталог назначения."
        }
        $Config.Archive.Sources = @($sources)
    }.GetNewClosure()

    $btnSave.Add_Click({
        try {
            if (-not (Test-Administrator)) { throw "Запустите настройщик от имени администратора." }
            & $applyToConfig
            Save-AgentConfig $Config $Path
            [System.Windows.Forms.MessageBox]::Show("Настройки сохранены.", "BifitReport",
                [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Ошибка",
                [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        }
    }.GetNewClosure())

    $btnSaveRestart.Add_Click({
        try {
            if (-not (Test-Administrator)) { throw "Запустите настройщик от имени администратора." }
            & $applyToConfig
            Save-AgentConfig $Config $Path
            $service = Get-Service -Name 'BifitReport' -ErrorAction SilentlyContinue
            if (-not $service) { throw "Служба BifitReport не установлена." }
            Restart-Service -Name 'BifitReport' -Force
            $lblService.Text = "Служба BifitReport: " + (Get-ServiceLabel)
            [System.Windows.Forms.MessageBox]::Show("Настройки сохранены, служба перезапущена.", "BifitReport",
                [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Ошибка",
                [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        }
    }.GetNewClosure())

    $btnOpenState.Add_Click({
        try {
            $stateDirectory = [string]$Config.Agent.StateDirectory
            if (-not (Test-Path -LiteralPath $stateDirectory)) { throw "Каталог ещё не создан: $stateDirectory" }
            Start-Process -FilePath explorer.exe -ArgumentList $stateDirectory
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Ошибка",
                [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        }
    }.GetNewClosure())

    $btnToken.Add_Click({
        try {
            if (-not (Test-Administrator)) { throw "Запустите настройщик от имени администратора." }
            $token = Show-TokenDialog $form
            if ([string]::IsNullOrWhiteSpace($token)) { return }
            Save-Token $Config $token
            $lblToken.Text = "Токен VK: " + (Get-TokenStatus $Config)
            [System.Windows.Forms.MessageBox]::Show("Токен сохранён.", "BifitReport",
                [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
        }
        catch {
            [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Ошибка",
                [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
        }
    }.GetNewClosure())

    $btnCheckVk.Add_Click({
        try {
            $outputVk.Clear()
            $groupResponse = Invoke-VkApi $Config 'groups.getById' @{}
            $group = @($groupResponse.groups) | Select-Object -First 1
            if ($group) {
                $outputVk.AppendText("Сообщество: $($group.name) (id $($group.id))`r`n")
            }
            $dialogs = Invoke-VkApi $Config 'messages.getConversations' @{ count = 20 }
            $outputVk.AppendText("Доступные диалоги:`r`n")
            foreach ($item in @($dialogs.items)) {
                $outputVk.AppendText(("  peer_id={0}  type={1}  can_write={2}`r`n" -f `
                    $item.conversation.peer.id,
                    $item.conversation.peer.type,
                    $item.conversation.can_write.allowed))
            }
        }
        catch {
            $outputVk.AppendText("ОШИБКА: $($_.Exception.Message)`r`n")
        }
    }.GetNewClosure())

    $btnSendTest.Add_Click({
        try {
            $peers = & $readPeers
            if ($peers.Count -eq 0) { throw "Сначала укажите peer_id." }
            $randomId = Get-Random -Minimum 1 -Maximum 2147483647
            $result = Invoke-VkApi $Config 'messages.send' @{
                peer_id = $peers[0]
                random_id = $randomId
                message = "BIFIT Report: тест настроек. Если вы видите это сообщение, доставка работает."
            }
            $outputVk.AppendText("Тест отправлен: peer_id=$($peers[0]), message_id=$result`r`n")
        }
        catch {
            $outputVk.AppendText("ОШИБКА: $($_.Exception.Message)`r`n")
        }
    }.GetNewClosure())

    $refreshDiagnostics = {
        $builder = New-Object System.Text.StringBuilder
        [void]$builder.AppendLine("Служба BifitReport: " + (Get-ServiceLabel))
        $tokenStatus = Get-TokenStatus $Config
        [void]$builder.AppendLine("Токен VK: " + $tokenStatus)
        [void]$builder.AppendLine("Конфигурация: " + $Path)
        [void]$builder.AppendLine()

        $stateDirectory = [string]$Config.Agent.StateDirectory
        $statusPath = Join-Path $stateDirectory 'status.json'
        if (Test-Path -LiteralPath $statusPath) {
            [void]$builder.AppendLine("status.json:")
            [void]$builder.AppendLine((Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8))
        }
        else {
            [void]$builder.AppendLine("status.json отсутствует.")
        }
        [void]$builder.AppendLine()

        $archivePath = Join-Path $stateDirectory 'archive-status.json'
        if (Test-Path -LiteralPath $archivePath) {
            [void]$builder.AppendLine("archive-status.json:")
            [void]$builder.AppendLine((Get-Content -LiteralPath $archivePath -Raw -Encoding UTF8))
        }
        [void]$builder.AppendLine()

        $pending = Join-Path $stateDirectory 'queue\pending'
        $sent = Join-Path $stateDirectory 'queue\sent'
        $pendingCount = if (Test-Path -LiteralPath $pending) { (Get-ChildItem -LiteralPath $pending -File | Measure-Object).Count } else { 0 }
        $sentCount = if (Test-Path -LiteralPath $sent) { (Get-ChildItem -LiteralPath $sent -File | Measure-Object).Count } else { 0 }
        [void]$builder.AppendLine("Очередь: ожидает $pendingCount, отправлено $sentCount")
        $outputDiag.Text = $builder.ToString()
    }.GetNewClosure()

    $btnRefresh.Add_Click({ & $refreshDiagnostics }.GetNewClosure())
    $form.Add_Shown({ & $refreshDiagnostics }.GetNewClosure())

    if (-not [string]::IsNullOrWhiteSpace($InitialTab)) {
        $initialPage = $tabs.TabPages | Where-Object { $_.Text -eq $InitialTab } | Select-Object -First 1
        if ($initialPage) { $tabs.SelectedTab = $initialPage }
    }

    [void]$form.ShowDialog()
    $form.Dispose()
}

# ---------------- Точка входа ----------------
$resolvedConfigPath = Resolve-ConfigPath $ConfigPath

if ($SelfTest) {
    $config = Read-AgentConfig $resolvedConfigPath
    "Конфигурация: $resolvedConfigPath"
    "StoreName: $($config.Reports.StoreName)"
    "Режим: $($config.Delivery.Mode)"
    "PeerIds: $((@($config.Vk.PeerIds) | Where-Object { $_ }) -join ', ')"
    "Архив: Enabled=$($config.Archive.Enabled) Destination=$($config.Archive.Destination)"
    "Источники: $(Format-Sources $config.Archive.Sources)"
    "Токен: $(Get-TokenStatus $config)"
    "Служба: $(Get-ServiceLabel)"

    $roundTrip = $config | ConvertTo-Json -Depth 20 | ConvertFrom-Json
    if (-not $roundTrip.Agent -or -not $roundTrip.Reports -or -not $roundTrip.Vk -or -not $roundTrip.Archive) {
        throw "Проверка JSON не пройдена: потеряны секции конфигурации."
    }
    "JSON round-trip: OK"

    $dummy = "selftest-" + [Guid]::NewGuid().ToString('N')
    $bytes = [Text.Encoding]::UTF8.GetBytes($dummy)
    $protected = [Security.Cryptography.ProtectedData]::Protect($bytes, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    $plain = [Security.Cryptography.ProtectedData]::Unprotect($protected, $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    try {
        if ([Text.Encoding]::UTF8.GetString($plain) -ne $dummy) { throw "Проверка DPAPI не пройдена." }
        "DPAPI: OK"
    }
    finally { [Array]::Clear($plain, 0, $plain.Length); [Array]::Clear($bytes, 0, $bytes.Length) }

    exit 0
}

if (-not (Test-Administrator) -and -not $NoElevate) {
    $argumentLine = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -ConfigPath `"$resolvedConfigPath`""
    if (-not [string]::IsNullOrWhiteSpace($Tab)) { $argumentLine += " -Tab `"$Tab`"" }
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $argumentLine
    return
}

try {
    $agentConfig = Read-AgentConfig $resolvedConfigPath
}
catch {
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "BifitReport — настройки",
        [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}
Show-SettingsGui $agentConfig $resolvedConfigPath $Tab