using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace DeepSeekWidget {

    public class BalanceInfo {
        public bool HasKey;
        public bool IsAvailable = true;
        public string Currency = "CNY";
        public decimal Total;
        public decimal Granted;
        public decimal ToppedUp;
        public string Error = "";
    }

    public class DayStat {
        public DateTime Date;
        public decimal Cost;
    }

    public class UsageInfo {
        public bool HasSession;
        public bool SessionExpired;
        public bool ParseFailed;
        public decimal CostToday;
        public long TokensToday;
        public long CacheHitTokensToday;
        public long CacheMissTokensToday;
        public bool HasTokenBreakdown;
        public string RawCost = "";
        public string RawAmount = "";
        public string Error = "";
        public readonly List<DayStat> Daily = new List<DayStat>();
    }

    public static class ApiClient {

        static ApiClient() {
            // 无 app.config 的 .NET Framework 程序默认 SecurityProtocol 仅 Ssl3|Tls（不含 TLS 1.2），
            // 而 platform.deepseek.com 的 WAF 只接受 TLS 1.2+，会导致"未能创建 SSL/TLS 安全通道"。
            // 优先启用 TLS 1.2/1.3；旧运行时（<4.8）不支持 Tls13 时回退到仅 TLS 1.2。
            try {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288);
            } catch {
                try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }
            }
        }

        // 校验登录凭据是否可用；返回 null 表示有效，否则返回错误说明
        public static async Task<string> ValidateTokenAsync(string token, string cookieHeader) {
            if (string.IsNullOrEmpty(token)) return "未获取到登录凭据";
            var bal = await FetchPlatformBalanceAsync(token, cookieHeader).ConfigureAwait(false);
            if (bal != null && bal.HasKey && bal.Error.Length == 0) return null;
            return bal == null || bal.Error.Length == 0 ? "验证失败" : bal.Error;
        }

        // 每次请求使用全新 HttpClient（全新连接），避免长期运行后连接池被污染
        // 导致持续收到异常响应（曾出现"余额获取失败/未找到余额数据"而外部测试正常的情况）
        static HttpClient NewClient() {
            var client = new HttpClient(
                new HttpClientHandler {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                }) {
                Timeout = TimeSpan.FromSeconds(15)
            };
            try { client.DefaultRequestHeaders.UserAgent.ParseAdd("DeepSeekWidget/1.0"); } catch { }
            return client;
        }

        // 通过平台登录态获取余额（无需 API Key）：GET /api/v0/users/get_user_summary
        public static async Task<BalanceInfo> FetchPlatformBalanceAsync(string token, string cookieHeader) {
            var info = new BalanceInfo { HasKey = !string.IsNullOrEmpty(token) };
            if (!info.HasKey) return info;
            // 最多尝试 2 次，每次全新连接，应对偶发的连接/响应异常
            for (int attempt = 0; attempt < 2; attempt++) {
                string body = null;
                int status = 0;
                string error;
                try {
                    using (var client = NewClient())
                    using (var req = new HttpRequestMessage(HttpMethod.Get, "https://platform.deepseek.com/api/v0/users/get_user_summary")) {
                        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                        if (!string.IsNullOrEmpty(cookieHeader)) {
                            req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                        }
                        using (var resp = await client.SendAsync(req).ConfigureAwait(false)) {
                            body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                            status = (int)resp.StatusCode;
                            if (status == 401 || status == 403) {
                                error = "平台登录已过期，请重新登录";
                            } else if (status == 429) {
                                error = "请求过于频繁，请稍后再试";
                            } else if (!resp.IsSuccessStatusCode) {
                                error = "HTTP " + status;
                            } else {
                                error = ParseSummaryBody(body, info);
                                if (error == null) return info; // 解析成功
                            }
                        }
                    }
                } catch (Exception ex) {
                    error = FriendlyNetworkError(ex);
                    body = null;
                }
                // 仅在 200 但结构无法解析时保存原始响应；登录过期等不落盘，避免泄漏凭据相关响应
                if (attempt == 0 && status == 200 && body != null) DumpBalance(body);
                info.Error = error;
            }
            return info;
        }

        // 解析 get_user_summary：取 normal_wallets（充值余额），赠送余额不再展示
        static string ParseSummaryBody(string body, BalanceInfo info) {
            var root = Json.Parse(body) as Dictionary<string, object>;
            if (root == null) return "响应格式异常（原始响应已存调试目录）";
            var walletObj = FindObjectWithKeys(root, new[] { "normal_wallets" });
            Dictionary<string, object> pick = null;
            foreach (object o in AsArray(Get(walletObj, "normal_wallets"))) {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                if (pick == null) pick = d;
                if (string.Equals(Str(d, "currency"), "CNY", StringComparison.OrdinalIgnoreCase)) {
                    pick = d;
                    break;
                }
            }
            if (pick == null) return "未找到余额数据（原始响应已存调试目录）";
            info.Currency = Str(pick, "currency") ?? "CNY";
            info.Total = Dec(pick, "balance");
            info.ToppedUp = info.Total;
            info.Granted = 0;
            info.IsAvailable = true;
            return null;
        }

        static string FriendlyNetworkError(Exception ex) {
            if (ex is System.Threading.Tasks.TaskCanceledException) return "请求超时，请检查网络或代理";
            var wex = ex as System.Net.WebException;
            if (wex != null) {
                string detail = "";
                if (wex.InnerException != null) detail = "（" + wex.InnerException.Message + "）";
                if (wex.Status == System.Net.WebExceptionStatus.Timeout) return "网络连接超时" + detail;
                if (wex.Status == System.Net.WebExceptionStatus.ConnectFailure
                    || wex.Status == System.Net.WebExceptionStatus.NameResolutionFailure) {
                    return "网络无法连接，请检查网络或代理设置" + detail;
                }
                if (wex.Status == System.Net.WebExceptionStatus.ReceiveFailure
                    || wex.Status == System.Net.WebExceptionStatus.SendFailure
                    || wex.Status == System.Net.WebExceptionStatus.ConnectionClosed) {
                    // 平台 WAF 可能重置连接，提示稍后自动重试
                    return "连接被中断（" + wex.Status + "），已自动重试" + detail;
                }
                return "请求失败（" + wex.Status + "）" + detail;
            }
            return ex.Message;
        }

        static void DumpBalance(string body) {
            try {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekWidget", "debug");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                File.WriteAllText(Path.Combine(dir, "balance_" + stamp + ".json"), body);
            } catch {
            }
        }

        public static async Task<UsageInfo> FetchUsageAsync(string token, string cookieHeader) {
            var info = new UsageInfo { HasSession = !string.IsNullOrEmpty(token) };
            if (!info.HasSession) return info;
            // 最多尝试 2 次，每次全新连接；登录过期不重试（重试无效）
            for (int attempt = 0; attempt < 2; attempt++) {
                info = await TryFetchUsageAsync(token, cookieHeader, info).ConfigureAwait(false);
                if (info.SessionExpired || (info.Error.Length == 0 && !info.ParseFailed)) return info;
                if (attempt == 0) DumpDebug(info); // 首次失败保存原始响应，便于排查
            }
            return info;
        }

        static async Task<UsageInfo> TryFetchUsageAsync(string token, string cookieHeader, UsageInfo info) {
            info.Error = "";
            info.ParseFailed = false;
            info.SessionExpired = false;
            info.RawCost = "";
            info.RawAmount = "";
            string year = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
            string month = DateTime.Now.Month.ToString(CultureInfo.InvariantCulture);
            try {
                // 串行请求（避免并发 TLS 连接被平台 WAF 判定异常导致连接被重置）
                var costResp = await GetJsonAsync("https://platform.deepseek.com/api/v0/usage/cost?year=" + year + "&month=" + month, token, cookieHeader).ConfigureAwait(false);
                var amountResp = await GetJsonAsync("https://platform.deepseek.com/api/v0/usage/amount?year=" + year + "&month=" + month, token, cookieHeader).ConfigureAwait(false);
                info.RawCost = costResp.Body;
                info.RawAmount = amountResp.Body;

                if (costResp.Status == 401 || costResp.Status == 403 || amountResp.Status == 401 || amountResp.Status == 403) {
                    info.SessionExpired = true;
                    info.Error = "平台登录已过期，请重新登录";
                    return info;
                }
                if (costResp.Status == 429 || amountResp.Status == 429) {
                    info.Error = "请求过于频繁，请稍后再试";
                    return info;
                }
                if (costResp.Status != 200 || amountResp.Status != 200) {
                    info.Error = "HTTP " + costResp.Status + "/" + amountResp.Status;
                    return info;
                }

                object costRoot = ParseJson(costResp.Body);
                object amountRoot = ParseJson(amountResp.Body);

                int? costCode = CodeOf(costRoot);
                if (costCode.HasValue && IsErrorCode(costCode.Value)) {
                    if (costCode.Value == 40002 || costCode.Value == 40003) {
                        info.SessionExpired = true;
                        info.Error = "平台登录已过期，请重新登录";
                    } else {
                        info.Error = "平台返回错误码 " + costCode.Value;
                    }
                    return info;
                }
                int? amountCode = CodeOf(amountRoot);
                if (amountCode.HasValue && IsErrorCode(amountCode.Value)) {
                    if (amountCode.Value == 40002 || amountCode.Value == 40003) {
                        info.SessionExpired = true;
                        info.Error = "平台登录已过期，请重新登录";
                    } else {
                        info.Error = "平台返回错误码 " + amountCode.Value;
                    }
                    return info;
                }

                var costData = FindObjectWithKeys(GetBizData(costRoot), CostDataKeys);
                if (costData != null) {
                    foreach (object o in AsArray(Get(costData, "days", "daily", "daily_cost", "dailyCost"))) {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        string rawDay = Str(d, "date", "day");
                        decimal cost = DayCost(d);
                        DateTime? day = ParseDay(rawDay);
                        if (day.HasValue) info.Daily.Add(new DayStat { Date = day.Value, Cost = cost });
                        if (IsToday(rawDay)) info.CostToday = cost;
                    }
                }

                var amountData = FindObjectWithKeys(GetBizData(amountRoot), AmountDataKeys);
                if (amountData != null) {
                    foreach (object o in AsArray(Get(amountData, "days", "daily", "daily_usage", "dailyUsage"))) {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        if (IsToday(Str(d, "date", "day"))) {
                            TokenBreakdown tokens = DayTokenBreakdown(d);
                            info.TokensToday = tokens.Total;
                            info.CacheHitTokensToday = tokens.CacheHit;
                            info.CacheMissTokensToday = tokens.CacheMiss;
                            info.HasTokenBreakdown = tokens.HasCacheBreakdown;
                            break;
                        }
                    }
                }
                return info;
            } catch (Exception ex) {
                info.ParseFailed = true;
                info.Error = FriendlyNetworkError(ex);
                DumpUsageError(ex);
                return info;
            }
        }

        static void DumpUsageError(Exception ex) {
            try {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekWidget", "debug");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                File.WriteAllText(Path.Combine(dir, "usage_error_" + stamp + ".txt"), ex.ToString());
            } catch {
            }
        }

        static readonly string[] CostDataKeys = {
            "cost", "costs", "currencies", "days", "daily", "daily_cost", "dailyCost", "total", "totals"
        };

        static readonly string[] AmountDataKeys = {
            "total", "totals", "days", "daily", "models", "model_usage", "modelUsage"
        };

        sealed class JsonResp {
            public int Status;
            public string Body = "";
        }

        static async Task<JsonResp> GetJsonAsync(string url, string token, string cookieHeader) {
            using (var client = NewClient())
            using (var req = new HttpRequestMessage(HttpMethod.Get, url)) {
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
                if (!string.IsNullOrEmpty(cookieHeader)) {
                    req.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                }
                using (var resp = await client.SendAsync(req).ConfigureAwait(false)) {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new JsonResp { Status = (int)resp.StatusCode, Body = body };
                }
            }
        }

        static object ParseJson(string body) {
            object root = Json.Parse(body);
            if (root == null) throw new InvalidOperationException("接口返回空内容");
            return root;
        }

        static bool IsErrorCode(int code) {
            return code != 0 && code != 200;
        }

        static int? CodeOf(object root) {
            var d = root as Dictionary<string, object>;
            if (d == null) return null;
            foreach (string k in new[] { "code", "status_code", "status" }) {
                if (!d.ContainsKey(k)) continue;
                object v = d[k];
                if (v is int) return (int)v;
                if (v is long) return (int)(long)v;
                if (v is string) {
                    int i;
                    if (int.TryParse((string)v, out i)) return i;
                }
            }
            return null;
        }

        // 去除平台的 {code,data:{biz_code,biz_data:{...}}} 包装
        static object GetBizData(object root) {
            object cur = root;
            int guard = 0;
            while (cur is Dictionary<string, object> && guard++ < 10) {
                var d = (Dictionary<string, object>)cur;
                if (d.ContainsKey("biz_data")) cur = d["biz_data"];
                else if (d.ContainsKey("bizData")) cur = d["bizData"];
                else break;
            }
            return cur;
        }

        // 广度优先查找第一个包含任一目标键的对象（对未知结构做容错）
        static Dictionary<string, object> FindObjectWithKeys(object root, string[] keys) {
            var queue = new Queue<object>();
            var seen = new HashSet<object>();
            if (root != null) queue.Enqueue(root);
            while (queue.Count > 0) {
                object cur = queue.Dequeue();
                if (cur == null || seen.Contains(cur)) continue;
                seen.Add(cur);
                var d = cur as Dictionary<string, object>;
                if (d != null) {
                    foreach (string k in keys) {
                        if (d.ContainsKey(k)) return d;
                    }
                    foreach (object v in d.Values) queue.Enqueue(v);
                } else {
                    var arr = cur as object[]; // JavaScriptSerializer 数组反序列化为 object[]
                    if (arr != null) {
                        foreach (object v in arr) queue.Enqueue(v);
                        continue;
                    }
                    var l = cur as ArrayList; // 兼容个别环境返回 ArrayList 的情况
                    if (l != null) {
                        foreach (object v in l) queue.Enqueue(v);
                    }
                }
            }
            return null;
        }

        static decimal DayCost(Dictionary<string, object> day) {
            decimal v = Dec(day, "amount", "value", "cost", "total");
            if (v != 0) return v;
            foreach (object o in AsArray(Get(day, "models", "data", "costs", "model_cost", "modelCost"))) {
                var md = o as Dictionary<string, object>;
                if (md == null) continue;
                var usage = AsArray(Get(md, "usage", "usages", "amounts", "values", "data"));
                if (usage.Count > 0) {
                    foreach (object u in usage) {
                        var ud = u as Dictionary<string, object>;
                        if (ud != null) v += Dec(ud, "amount", "value", "cost");
                    }
                } else {
                    v += Dec(md, "amount", "value", "cost");
                }
            }
            return v;
        }

        sealed class TokenBreakdown {
            public long Total;
            public long CacheHit;
            public long CacheMiss;
            public bool HasCacheBreakdown;
        }

        static TokenBreakdown DayTokenBreakdown(Dictionary<string, object> day) {
            var result = new TokenBreakdown();
            foreach (object o in AsArray(Get(day, "data", "models", "usage", "usages"))) {
                var md = o as Dictionary<string, object>;
                if (md == null) continue;
                foreach (object u in AsArray(Get(md, "usage", "usages", "amounts", "values", "data"))) {
                    var ud = u as Dictionary<string, object>;
                    if (ud == null) continue;
                    string type = Str(ud, "type", "usage_type", "usageType", "name", "key");
                    if (type == null) continue;
                    long amount = (long)Dec(ud, "amount", "value", "count", "total");
                    // 平台返回的 token 类型：PROMPT_TOKEN / PROMPT_CACHE_HIT_TOKEN /
                    // PROMPT_CACHE_MISS_TOKEN / RESPONSE_TOKEN（REQUEST 是请求次数，不计入）
                    if (type == "PROMPT_TOKEN" || type == "PROMPT_CACHE_HIT_TOKEN"
                        || type == "PROMPT_CACHE_MISS_TOKEN" || type == "RESPONSE_TOKEN") {
                        result.Total += amount;
                    }
                    if (type == "PROMPT_CACHE_HIT_TOKEN") {
                        result.CacheHit += amount;
                        result.HasCacheBreakdown = true;
                    } else if (type == "PROMPT_CACHE_MISS_TOKEN") {
                        result.CacheMiss += amount;
                        result.HasCacheBreakdown = true;
                    }
                }
            }
            return result;
        }

        static DateTime? ParseDay(string s) {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();
            int ti = s.IndexOf('T');
            if (ti > 0) s = s.Substring(0, ti);
            string[] formats = {
                "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d",
                "MM-dd", "M-d", "MM/dd", "M/d"
            };
            DateTime d;
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) {
                if (d.Year == 1) {
                    DateTime now = DateTime.Now;
                    d = new DateTime(now.Year, d.Month, d.Day);
                }
                return d.Date;
            }
            return null;
        }

        static bool IsToday(string s) {
            DateTime? d = ParseDay(s);
            if (!d.HasValue) return false;
            return d.Value == DateTime.Now.Date || d.Value == DateTime.UtcNow.Date;
        }

        static object Get(object o, params string[] keys) {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            foreach (string k in keys) {
                if (d.ContainsKey(k)) return d[k];
            }
            return null;
        }

        static List<object> AsArray(object o) {
            var l = o as ArrayList;
            if (l != null) return l.Cast<object>().ToList();
            var arr = o as object[]; // JavaScriptSerializer 将 JSON 数组反序列化为 object[]
            if (arr != null) return arr.ToList();
            var d = o as Dictionary<string, object>;
            if (d != null) return d.Values.ToList();
            return new List<object>();
        }

        static string Str(object o, params string[] keys) {
            object v = Get(o, keys);
            if (v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        static decimal Dec(object o, params string[] keys) {
            object v = Get(o, keys);
            if (v == null) return 0;
            try {
                return Convert.ToDecimal(v, CultureInfo.InvariantCulture);
            } catch {
                return 0;
            }
        }

        static void DumpDebug(UsageInfo info) {
            try {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekWidget", "debug");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.WriteAllText(Path.Combine(dir, "usage_cost_" + stamp + ".json"), info.RawCost);
                File.WriteAllText(Path.Combine(dir, "usage_amount_" + stamp + ".json"), info.RawAmount);
            } catch {
                // 调试文件写入失败忽略
            }
        }
    }
}

