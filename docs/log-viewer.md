# BIFIT Log Viewer

`BifitReport.LogViewer` — отдельная Windows-служба с веб-интерфейсом для просмотра живых и архивных журналов БИФИТ. Она не входит в процесс агента отчётов, не меняет исходные файлы и устанавливается независимо.

## Возможности

- обычная форма входа на ASP.NET Core Identity;
- локальная SQLite-база пользователей со стойкими хешами паролей, lockout и защищённой cookie-сессией;
- HTTPS с сертификатом из `LocalMachine\My`; удалённый plain HTTP запрещён валидатором конфигурации;
- список файлов только из явно заданных корней и glob-масок;
- просмотр последних строк, поиск и фильтрация по уровню;
- live-tail с обработкой дописывания, обрезки и замены файла;
- чтение `.log.gz` с лимитами на сжатый и распакованный размер;
- маскирование токенов, e-mail, телефонов, карт и длинных идентификаторов по умолчанию;
- rate limiting, security headers, readiness/liveness checks и отдельный аудит обращений;
- защита от path traversal и отказ от чтения через symlink/junction внутри разрешённого корня.

Сервис намеренно не даёт скачивать исходный файл. Raw-просмотр можно включить настройкой `AllowRawLogs`, но он доступен всем вошедшим пользователям и каждое открытие файла фиксируется в журнале сервиса.

## Сборка

По умолчанию получается self-contained публикация для Windows x64:

```powershell
.\scripts\publish-log-viewer.ps1
```

Результат: `artifacts\log-viewer\win-x64-selfcontained`. Для framework-dependent сборки:

```powershell
.\scripts\publish-log-viewer.ps1 -FrameworkDependent
```

## Подготовка сертификата

Для боевой установки нужен доверенный сертификат с:

- DNS-именем сервера в SAN;
- назначением `Server Authentication`;
- закрытым ключом в `Cert:\LocalMachine\My`;
- доверенной цепочкой на компьютерах операторов.

Используйте сертификат корпоративного УЦ или другого доверенного УЦ. Self-signed подходит только для изолированного стенда. Получить thumbprint:

```powershell
Get-ChildItem Cert:\LocalMachine\My |
    Where-Object HasPrivateKey |
    Select-Object Subject, Thumbprint, NotAfter
```

## Установка

PowerShell нужно запустить от администратора. Скрипт безопасно запросит пароль через `Read-Host -AsSecureString`: пароль не попадёт в конфигурацию или командную строку. Минимальная длина — 12 символов; обязательны верхний и нижний регистр, цифра и специальный символ.

```powershell
.\scripts\install-log-viewer-service.ps1 `
  -CertificateThumbprint "0123456789ABCDEF0123456789ABCDEF01234567" `
  -ListenUrl "https://0.0.0.0:7443" `
  -LogRoot "C:\ProgramData\BIFIT\retail\logs" `
  -AdminUser "admin" `
  -OpenFirewall
```

`-OpenFirewall` создаёт входящее правило только для профилей Domain и Private, выбранного TCP-порта и exe сервиса. На Public-профиле порт не открывается.

После установки откройте `https://имя-сервера:7443/` и войдите с логином `admin` и паролем, заданным при установке. Эндпоинты мониторинга:

- `/health/live` — процесс отвечает;
- `/health/ready` — доступен хотя бы один настроенный источник журналов.

Они не требуют авторизации и возвращают только итоговый статус без путей и содержимого журналов.

## Обновление

Сначала опубликуйте новую версию, затем сохраните установленную конфигурацию:

```powershell
.\scripts\install-log-viewer-service.ps1 -PreserveConfiguration
```

Скрипт останавливает службу, заменяет файлы, возвращает прежний `appsettings.json`, настраивает recovery и запускает службу.

Сброс пароля существующего пользователя:

```powershell
$password = Read-Host "Новый пароль" -AsSecureString
.\scripts\install-log-viewer-service.ps1 `
  -PreserveConfiguration `
  -AdminUser "admin" `
  -AdminPassword $password `
  -ResetAdminPassword
```

## Конфигурация

Основные секции `appsettings.json`:

```json
{
  "Server": {
    "ListenUrl": "https://0.0.0.0:7443",
    "CertificateThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567"
  },
  "Authentication": {
    "DatabasePath": "C:\\ProgramData\\BifitReport.LogViewer\\identity.db",
    "DataProtectionKeysPath": "C:\\ProgramData\\BifitReport.LogViewer\\keys",
    "SessionHours": 8
  },
  "LogViewer": {
    "Sources": [
      {
        "Id": "bifit",
        "DisplayName": "Касса 1",
        "RootPath": "C:\\ProgramData\\BIFIT\\retail\\logs",
        "Includes": [ "**/*.log", "**/*.log.gz" ],
        "Recursive": true
      }
    ],
    "AllowRawLogs": false
  }
}
```

Можно добавить несколько источников. `Id` должен быть уникальным и состоять из строчных латинских букв, цифр и дефисов. Пути не возвращаются клиенту: интерфейс получает только относительные имена.

Переменные окружения используют префикс `BIFIT_LOG_VIEWER_`, например:

```text
BIFIT_LOG_VIEWER_LogViewer__AllowRawLogs=false
```

Локальная разработка разрешена по HTTP только на loopback. Создайте игнорируемый `src\BifitReport.LogViewer\appsettings.local.json`, задайте в нём пути Identity внутри тестового каталога и один раз создайте пользователя:

```powershell
$password = Read-Host "Пароль" -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    $plain | dotnet run --project .\src\BifitReport.LogViewer\BifitReport.LogViewer.csproj -c Release -- bootstrap-user --username admin
}
finally {
    $plain = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}
dotnet run --project .\src\BifitReport.LogViewer\BifitReport.LogViewer.csproj -c Release
```

## Эксплуатация и аудит

Служба называется `BifitReport.LogViewer`. Её собственные журналы хранятся отдельно от состояния агента отчётов, а установщик ограничивает каталог ACL для `SYSTEM` и локальных администраторов. Ротация выполняется по дням и размеру:

```text
C:\ProgramData\BifitReport.LogViewer\logs
```

В аудите есть имя пользователя Log Viewer, источник, относительный путь, режим raw и удалённый адрес. Поисковая строка и содержимое кассового журнала в аудит не записываются.

Каталоги с журналами должны оставаться read-only для учётной записи службы. Служба работает от `LocalSystem`, поэтому для сетевых источников рекомендуется отдельная service account с минимальными правами чтения и явная настройка службы через SCM.

## Удаление

```powershell
.\scripts\uninstall-log-viewer-service.ps1 -RemoveFirewallRule
```

База пользователей, ключи cookie и аудит сохраняются. Для их удаления добавьте `-RemoveApplicationData`; эта операция необратима. Исходные журналы БИФИТ скрипт не удаляет и не изменяет.
