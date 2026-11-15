using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;

namespace BifitReport.Settings;

internal static class SelfTest
{
    public static bool Run(Options options)
    {
        var lines = new List<string>();
        var ok = true;

        try
        {
            var config = ConfigFile.Load(options.ConfigPath);
            lines.Add("Конфигурация: " + config.Path);
            lines.Add("StoreName: " + config.StoreName);
            lines.Add("Режим: " + config.Mode);
            lines.Add("PeerIds: " + string.Join(", ", config.GetPeers()));
            lines.Add("Архив: Enabled=" + config.GetBool("Archive", "Enabled", false) +
                      " Destination=" + config.GetString("Archive", "Destination"));
            lines.Add("Источники: " + string.Join(
                "; ",
                config.GetSources().Select(source => source.Name + "|" + source.Path)));
            lines.Add("Токен: " + TokenStore.Status(config));
            lines.Add("Служба: " + ServiceLabel());

            var temporary = Path.Combine(
                Path.GetTempPath(),
                "bifit-settings-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                config.SaveTo(temporary);
                var reloaded = ConfigFile.Load(temporary);
                if (reloaded.StoreName != config.StoreName)
                {
                    throw new InvalidOperationException("StoreName не совпал после перезаписи.");
                }

                if (reloaded.GetPeers().Length != config.GetPeers().Length)
                {
                    throw new InvalidOperationException("PeerIds не совпали после перезаписи.");
                }

                lines.Add("JSON round-trip: OK");
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }

            var dummy = "selftest-" + Guid.NewGuid().ToString("N");
            var bytes = Encoding.UTF8.GetBytes(dummy);
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
            try
            {
                if (Encoding.UTF8.GetString(plainBytes) != dummy)
                {
                    throw new InvalidOperationException("DPAPI вернул другие данные.");
                }

                lines.Add("DPAPI: OK");
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
                Array.Clear(plainBytes, 0, plainBytes.Length);
            }

            if (TokenStore.Status(config) == "сохранён")
            {
                try
                {
                    var vk = new VkClient(config);
                    lines.Add("VK: OK");
                    lines.Add(vk.Check().Trim());
                }
                catch (Exception exception)
                {
                    lines.Add("VK: ОШИБКА " + exception.Message);
                    ok = false;
                }
            }
            else
            {
                lines.Add("VK: пропущено (токен не сохранён)");
            }
        }
        catch (Exception exception)
        {
            ok = false;
            lines.Add("ОШИБКА: " + exception);
        }

        try
        {
            File.WriteAllText(
                options.SelfTestOutput,
                string.Join(Environment.NewLine, lines),
                new UTF8Encoding(false));
        }
        catch
        {
            // Если файл отчёта записать нельзя, остаётся только код возврата.
        }

        return ok;
    }

    private static string ServiceLabel()
    {
        try
        {
            using var service = new ServiceController("BifitReport");
            return service.Status.ToString();
        }
        catch (InvalidOperationException)
        {
            return "не установлена";
        }
        catch (Exception exception)
        {
            return "ошибка: " + exception.Message;
        }
    }
}
