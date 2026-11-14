using System.Security.Cryptography;
using BifitReport.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace BifitReport.Agent.Delivery;

public sealed class VkTokenProvider(IOptions<VkOptions> options)
{
    private const string EnvironmentVariableName = "BIFIT_REPORT_VK_TOKEN";
    private readonly VkOptions _options = options.Value;

    public string GetToken()
    {
        var environmentToken = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentToken)) return environmentToken.Trim();

        var tokenPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.TokenFile));
        if (!File.Exists(tokenPath))
        {
            throw new InvalidOperationException(
                $"Токен VK не найден. Запустите scripts\\set-vk-token.ps1 или задайте {EnvironmentVariableName}.");
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Расшифровка токена через DPAPI поддерживается только в Windows.");
        }

        var protectedBytes = File.ReadAllBytes(tokenPath);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        try
        {
            var token = System.Text.Encoding.UTF8.GetString(bytes).Trim();
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Файл токена VK пуст.");
            return token;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
