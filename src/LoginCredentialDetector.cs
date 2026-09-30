using System;
using System.Collections.Generic;

namespace DeepSeekWidget {

    public sealed class LoginCandidate {
        public string Token = "";
        public string Key = "";
        public int Score;
    }

    public static class LoginCredentialDetector {

        static readonly char[] InvalidTokenChars = { ' ', '\t', '\r', '\n' };

        public static LoginCandidate SelectBest(string scanJson) {
            List<LoginCandidate> candidates = ParseCandidates(scanJson);
            return candidates.Count == 0 ? null : candidates[0];
        }

        public static List<LoginCandidate> ParseCandidates(string scanJson) {
            var result = new List<LoginCandidate>();
            Dictionary<string, object> root = Json.Parse(scanJson) as Dictionary<string, object>;
            if (root == null) return result;

            object[] found = AsArray(Get(root, "found"));
            var byToken = new Dictionary<string, LoginCandidate>(StringComparer.Ordinal);
            foreach (object item in found) {
                var map = item as Dictionary<string, object>;
                if (map == null) continue;
                string key = Convert.ToString(Get(map, "key")) ?? "";
                string token = Convert.ToString(Get(map, "value")) ?? "";
                if (!LooksLikeCredential(token)) continue;

                var candidate = new LoginCandidate {
                    Token = token,
                    Key = key,
                    Score = Score(key, token)
                };

                LoginCandidate old;
                if (!byToken.TryGetValue(token, out old) || candidate.Score > old.Score) {
                    byToken[token] = candidate;
                }
            }

            result.AddRange(byToken.Values);
            result.Sort((a, b) => {
                int score = b.Score.CompareTo(a.Score);
                return score != 0 ? score : string.CompareOrdinal(a.Key, b.Key);
            });
            return result;
        }

        static bool LooksLikeCredential(string value) {
            if (string.IsNullOrEmpty(value) || value.Length < 16 || value.Length > 4096) return false;
            if (value.IndexOfAny(InvalidTokenChars) >= 0) return false;
            foreach (char c in value) {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '~'
                    || c == '+' || c == '/' || c == '=' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        static int Score(string key, string token) {
            int score = token.Length;
            string k = (key ?? "").ToLowerInvariant();
            if (k.Contains("token")) score += 100000;
            if (k.Contains("auth")) score += 90000;
            if (k.Contains("session")) score += 85000;
            if (k.Contains("jwt")) score += 80000;
            if (k.Contains("access")) score += 70000;
            if (k.Contains("cookie")) score += 20000;
            if (k.Contains("user")) score += 50000;

            if (k.Contains("analytics") || k.Contains("visitor") || k.Contains("tracking")
                || k.Contains("device") || k.Contains("locale") || k.Contains("theme")
                || k.Contains("config")) {
                score -= 60000;
            }
            return score;
        }

        static object Get(Dictionary<string, object> map, string key) {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        static object[] AsArray(object value) {
            var array = value as object[];
            if (array != null) return array;
            var list = value as List<object>;
            return list == null ? new object[0] : list.ToArray();
        }
    }
}
