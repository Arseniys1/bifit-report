using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using IOPath = System.IO.Path;

namespace BifitReport.Settings;

internal sealed class ArchiveSourceEntry
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Редактируемое представление appsettings.json. Неизвестные поля сохраняются,
/// потому что файл читается как JObject, а не через строгую модель.
/// </summary>
internal sealed class ConfigFile
{
    private static readonly string[] Sections = ["Agent", "Delivery", "Vk", "Reports", "Archive"];

    private ConfigFile(string path, JObject root)
    {
        Path = path;
        Root = root;
    }

    public string Path { get; }

    public JObject Root { get; }

    public string StateDirectory => GetString("Agent", "StateDirectory", @"C:\ProgramData\BifitReport");

    public string LogPath => GetString("Agent", "LogPath", @"C:\ProgramData\BIFIT\retail\logs\desktop.log");

    public string StoreName => GetString("Reports", "StoreName", string.Empty);

    public string Mode => GetString("Delivery", "Mode", "File");

    public static ConfigFile Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Файл настроек не найден:{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}" +
                "Сначала установите службу: scripts\\install-service.ps1",
                path);
        }

        var root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
        foreach (var section in Sections)
        {
            if (root[section] is not JObject)
            {
                root[section] = new JObject();
            }
        }

        return new ConfigFile(IOPath.GetFullPath(path), root);
    }

    public void Save() => SaveTo(Path);

    public void SaveTo(string path)
    {
        var directory = IOPath.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = Root.ToString(Formatting.Indented);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        if (File.Exists(path))
        {
            File.Replace(temporary, path, null);
        }
        else
        {
            File.Move(temporary, path);
        }
    }

    public string GetString(string section, string property, string fallback = "")
    {
        var token = Section(section)[property];
        if (token == null || token.Type == JTokenType.Null) return fallback;
        return token.Type == JTokenType.String ? token.Value<string>() ?? fallback : token.ToString();
    }

    public int GetInt(string section, string property, int fallback)
    {
        var token = Section(section)[property];
        if (token != null && token.Type == JTokenType.Integer) return token.Value<int>();
        return int.TryParse(token?.ToString(), out var value) ? value : fallback;
    }

    public bool GetBool(string section, string property, bool fallback)
    {
        var token = Section(section)[property];
        if (token != null && token.Type == JTokenType.Boolean) return token.Value<bool>();
        return bool.TryParse(token?.ToString(), out var value) ? value : fallback;
    }

    public long[] GetPeers()
    {
        if (Section("Vk")["PeerIds"] is not JArray peers) return [];

        return peers
            .Select(token => long.TryParse(token.ToString(), out var peerId) ? peerId : 0L)
            .Where(peerId => peerId > 0)
            .ToArray();
    }

    public IReadOnlyList<ArchiveSourceEntry> GetSources()
    {
        if (Section("Archive")["Sources"] is not JArray sources) return [];

        return sources
            .OfType<JObject>()
            .Select(item => new ArchiveSourceEntry
            {
                Name = (string?)item["Name"] ?? string.Empty,
                Path = (string?)item["Path"] ?? string.Empty
            })
            .Where(source => source.Name.Length > 0 && source.Path.Length > 0)
            .ToArray();
    }

    public IReadOnlyList<string> GetArchivePatterns()
    {
        if (Section("Archive")["Patterns"] is not JArray patterns) return [];

        return patterns
            .Values<string>()
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Select(pattern => pattern!.Trim())
            .ToArray();
    }

    public void SetString(string section, string property, string value) =>
        Section(section)[property] = value;

    public void SetInt(string section, string property, int value) =>
        Section(section)[property] = value;

    public void SetBool(string section, string property, bool value) =>
        Section(section)[property] = value;

    public void SetPeers(IEnumerable<long> peers) =>
        Section("Vk")["PeerIds"] = new JArray(peers.Select(peerId => (object)peerId));

    public void SetSources(IEnumerable<ArchiveSourceEntry> sources) =>
        Section("Archive")["Sources"] = new JArray(sources.Select(source => new JObject
        {
            ["Name"] = source.Name,
            ["Path"] = source.Path
        }));

    public void SetArchivePatterns(IEnumerable<string> patterns) =>
        Section("Archive")["Patterns"] = new JArray(patterns.Select(pattern => (object)pattern));

    public string ResolveTokenPath()
    {
        var raw = GetString("Vk", "TokenFile");
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = IOPath.Combine(StateDirectory, "secrets", "vk-token.dat");
        }

        return IOPath.GetFullPath(Environment.ExpandEnvironmentVariables(raw));
    }

    private JObject Section(string name) => (JObject)Root[name]!;
}
