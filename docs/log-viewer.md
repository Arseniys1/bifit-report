# BIFIT Log Viewer

`BifitReport.LogViewer` — отдельная Windows-служба с веб-интерфейсом для просмотра живых и архивных журналов БИФИТ. Она не входит в процесс агента отчётов, не меняет исходные файлы и устанавливается независимо.

## Возможности

- Windows Integrated Authentication (Negotiate/Kerberos/NTLM) без собственной базы паролей;
- allow-list пользователей и групп Windows;
- HTTPS с сертификатом из `LocalMachine\My`; удалённый plain HTTP запрещён валидатором конфигурации;
- список файлов только из явно заданных корней и glob-масок;
- просмотр последних строк, поиск и фильтрация по уровню;
- live-tail с обработкой дописывания, обрезки и замены файла;
- чтение `.log.gz` с лимитами на сжатый и распакованный размер;
- маскирование токенов, e-mail, телефонов, карт и длинных идентификаторов по умолчанию;
- rate limiting, security headers, readiness/liveness checks и отдельный аудит обращений;
- защита от path traversal и отказ от чтения через symlink/junction внутри разрешённого корня.

Сервис намеренно не даёт скачивать исходный файл. Raw-просмотр можно включить настройкой `AllowRawLogs`, но он доступен всем пользователям из `AllowedPrincipals` и каждое открытие файла фиксируется в журнале сервиса.

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

PowerShell нужно запустить от администратора. В allow-list лучше указывать отдельную доменную группу операторов; локальные администраторы разрешены ниже только как стартовый пример.

```powershell
.\scripts\install-log-viewer-service.ps1 `
  -CertificateThumbprint "0123456789ABCDEF0123456789ABCDEF01234567" `
  -ListenUrl "https://0.0.0.0:7443" `
  -LogRoot "C:\ProgramData\BIFIT\retail\logs" `
  -AllowedPrincipals "CONTOSO\Bifit Log Readers" `
  -OpenFirewall
```

`-OpenFirewall` создаёт входящее правило только для профилей Domain и Private, выбранного TCP-порта и exe сервиса. На Public-профиле порт не открывается.

После установки откройте `https://имя-сервера:7443/`. Для автоматического входа браузер и политика Windows должны разрешать Integrated Authentication для этого узла. Эндпоинты мониторинга:

- `/health/live` — процесс отвечает;
- `/health/ready` — доступен хотя бы один настроенный источник журналов.

Они не требуют авторизации и возвращают только итоговый статус без путей и содержимого журналов.

## Обновление

Сначала опубликуйте новую версию, затем сохраните установленную конфигурацию:

```powershell
.\scripts\install-log-viewer-service.ps1 -PreserveConfiguration
```

Скрипт останавливает службу, заменяет файлы, возвращает прежний `appsettings.json`, настраивает recovery и запускает службу.

## Конфигурация

Основные секции `appsettings.json`:

```json
{
  "Server": {
    "ListenUrl": "https://0.0.0.0:7443",
    "CertificateThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567"
  },
  "Access": {
    "AllowedPrincipals": [ "CONTOSO\\Bifit Log Readers" ]
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

Локальная разработка разрешена по HTTP только на loopback. Создайте игнорируемый `src\BifitReport.LogViewer\appsettings.local.json` и запустите:

```powershell
dotnet run --project .\src\BifitReport.LogViewer\BifitReport.LogViewer.csproj -c Release
```

## Эксплуатация и аудит

Служба называется `BifitReport.LogViewer`. Её собственные журналы хранятся отдельно от состояния агента отчётов, а установщик ограничивает каталог ACL для `SYSTEM` и локальных администраторов. Ротация выполняется по дням и размеру:

```text
C:\ProgramData\BifitReport.LogViewer\logs
```

В аудите есть Windows-пользователь, источник, относительный путь, режим raw и удалённый адрес. Поисковая строка и содержимое кассового журнала в аудит не записываются.

Каталоги с журналами должны оставаться read-only для учётной записи службы. Служба работает от `LocalSystem`, поэтому для сетевых источников рекомендуется отдельная service account с минимальными правами чтения и явная настройка службы через SCM.

## Удаление

```powershell
.\scripts\uninstall-log-viewer-service.ps1 -RemoveFirewallRule
```

Аудит сохраняется. Для его удаления добавьте `-RemoveApplicationLogs`; эта операция необратима. Исходные журналы БИФИТ скрипт не удаляет и не изменяет.
