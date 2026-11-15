using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;

namespace BifitReport.Settings;

internal sealed class VkClient(ConfigFile config)
{
    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public JToken Call(string method, IDictionary<string, string> parameters)
    {
        var token = TokenStore.Read(config);
        var body = new Dictionary<string, string>
        {
            ["access_token"] = token,
            ["v"] = "5.199"
        };
        foreach (var pair in parameters)
        {
            body[pair.Key] = pair.Value;
        }

        using var content = new FormUrlEncodedContent(body);
        using var response = Http
            .PostAsync("https://api.vk.ru/method/" + method, content)
            .GetAwaiter()
            .GetResult();
        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var root = JObject.Parse(text);
        if (root["error"] is JObject error)
        {
            throw new InvalidOperationException(
                $"VK API {error["error_code"]}: {error["error_msg"]}");
        }

        return root["response"] ?? JValue.CreateNull();
    }

    public string Check()
    {
        var output = new StringBuilder();

        var groups = (JObject)Call("groups.getById", new Dictionary<string, string>());
        var group = (groups["groups"] as JArray)?.OfType<JObject>().FirstOrDefault();
        if (group != null)
        {
            output.AppendLine($"Сообщество: {group["name"]} (id {group["id"]})");
        }

        var dialogs = (JObject)Call(
            "messages.getConversations",
            new Dictionary<string, string> { ["count"] = "20" });
        output.AppendLine("Доступные диалоги:");
        foreach (var item in (dialogs["items"] as JArray) ?? new JArray())
        {
            if (item is not JObject conversationItem) continue;

            var conversation = conversationItem["conversation"] as JObject;
            var peer = conversation?["peer"] as JObject;
            var canWrite = (conversation?["can_write"] as JObject)?["allowed"];
            output.AppendLine(
                $"  peer_id={peer?["id"]}  type={peer?["type"]}  can_write={canWrite}");
        }

        return output.ToString();
    }

    public long SendTest(long peerId)
    {
        var response = Call(
            "messages.send",
            new Dictionary<string, string>
            {
                ["peer_id"] = peerId.ToString(CultureInfo.InvariantCulture),
                ["random_id"] = new Random().Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture),
                ["message"] = "BIFIT Report: тест настроек. Если вы видите это сообщение, доставка работает."
            });

        return response.Type == JTokenType.Integer ? response.Value<long>() : 0;
    }
}
