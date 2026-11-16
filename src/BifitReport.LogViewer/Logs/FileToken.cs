using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace BifitReport.LogViewer.Logs;

public static class FileToken
{
    private const int MaximumDecodedLength = 4_096;

    public static string Encode(string relativePath) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(relativePath));

    public static string Decode(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaximumDecodedLength * 2)
        {
            throw new LogRequestException("Некорректный идентификатор файла.");
        }

        try
        {
            var value = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            if (value.Length == 0 || value.Length > MaximumDecodedLength)
            {
                throw new LogRequestException("Некорректный идентификатор файла.");
            }
            return value;
        }
        catch (FormatException)
        {
            throw new LogRequestException("Некорректный идентификатор файла.");
        }
    }
}
