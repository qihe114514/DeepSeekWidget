using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DeepSeekWidget {

    // 取代 .NET Framework 的 JavaScriptSerializer：把 JSON 解析成
    // Dictionary<string, object> / object[] / string / long / decimal / bool / null
    internal static class Json {

        public static object Parse(string text) {
            if (string.IsNullOrEmpty(text)) return null;
            using (JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            })) {
                return Convert(doc.RootElement);
            }
        }

        static object Convert(JsonElement e) {
            switch (e.ValueKind) {
                case JsonValueKind.Object:
                    var map = new Dictionary<string, object>();
                    foreach (JsonProperty p in e.EnumerateObject()) map[p.Name] = Convert(p.Value);
                    return map;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (JsonElement item in e.EnumerateArray()) list.Add(Convert(item));
                    return list.ToArray();
                case JsonValueKind.String:
                    return e.GetString();
                case JsonValueKind.Number:
                    long l;
                    if (e.TryGetInt64(out l)) return l;
                    decimal m;
                    if (e.TryGetDecimal(out m)) return m;
                    return e.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }
    }
}
