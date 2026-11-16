using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using IOPath = System.IO.Path;

namespace BifitReport.Settings;

/// <summary>
/// Токен VK хранится в DPAPI-файле (LocalMachine), как и у агента.
/// Значение токена никогда не выводится в интерфейс.
/// </summary>
internal static class TokenStore
{
    public static string Status(ConfigFile config)
    {
        try
        {
            var path = config.ResolveTokenPath();
            if (!File.Exists(path)) return "не сохранён";

            var protectedBytes = File.ReadAllBytes(path);
            var plainBytes = ProtectedData.Unprotect(
                protectedBytes, null, DataProtectionScope.LocalMachine);
            try
            {
                var token = Encoding.UTF8.GetString(plainBytes);
                return string.IsNullOrWhiteSpace(token) ? "файл пуст" : "сохранён";
            }
            finally
            {
                Array.Clear(plainBytes, 0, plainBytes.Length);
            }
        }
        catch (Exception exception)
        {
            return "не читается: " + exception.Message;
        }
    }

    public static string Read(ConfigFile config)
    {
        var path = config.ResolveTokenPath();
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Токен VK не сохранён.");
        }

        var protectedBytes = File.ReadAllBytes(path);
        var plainBytes = ProtectedData.Unprotect(
            protectedBytes, null, DataProtectionScope.LocalMachine);
        try
        {
            var token = Encoding.UTF8.GetString(plainBytes).Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Файл токена VK пуст.");
            }

            return token;
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public static void Save(ConfigFile config, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Токен не может быть пустым.");
        }

        var path = config.ResolveTokenPath();
        var directory = IOPath.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Не удалось определить каталог токена.");

        Directory.CreateDirectory(directory);
        var plainBytes = Encoding.UTF8.GetBytes(token.Trim());
        try
        {
            var protectedBytes = ProtectedData.Protect(
                plainBytes, null, DataProtectionScope.LocalMachine);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, protectedBytes);
            if (File.Exists(path))
            {
                File.Replace(temporary, path, null);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }

        RestrictDirectory(directory);
    }

    private static void RestrictDirectory(string directory)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        var arguments = $"\"{directory}\" /inheritance:r /grant:r \"*S-1-5-18:(OI)(CI)F\" \"*S-1-5-32-544:(OI)(CI)F\"";
        if (!string.IsNullOrWhiteSpace(sid))
        {
            arguments += $" \"*{sid}:(OI)(CI)F\"";
        }

        var startInfo = new ProcessStartInfo("icacls.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Не удалось запустить icacls.exe.");
        if (!process.WaitForExit(15_000))
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Основная ошибка — превышение времени ожидания; не заменяем её
                // вторичной ошибкой завершения зависшего процесса.
            }

            throw new InvalidOperationException(
                "Токен сохранён, но icacls.exe не завершился за 15 секунд. " +
                "Проверьте права каталога секретов вручную.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Токен сохранён, но не удалось ограничить доступ к каталогу секретов. " +
                "Запустите настройщик от имени администратора.");
        }
    }
}
