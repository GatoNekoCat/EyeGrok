// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;

namespace EyeGrok.Net
{
    public static class JsonBody
    {
        public static string TextAndNonce(string text, string nonce)
        {
            return "{\"text\":" + Quote(text) + ",\"nonce\":" + Quote(nonce) + "}";
        }

        public static string Quote(string value)
        {
            var sb = new StringBuilder(value == null ? 2 : value.Length + 2);
            sb.Append('"');
            if (value != null)
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    switch (c)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '"': sb.Append("\\\""); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static bool TryReadText(string json, out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(json)) return false;
            var key = json.IndexOf("\"text\"", System.StringComparison.Ordinal);
            if (key < 0) return false;
            var colon = json.IndexOf(':', key + 6);
            if (colon < 0) return false;
            var start = json.IndexOf('"', colon + 1);
            if (start < 0) return false;
            var sb = new StringBuilder();
            for (var i = start + 1; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '\\')
                {
                    if (i + 1 >= json.Length) return false;
                    var n = json[++i];
                    switch (n)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 >= json.Length) return false;
                            int code;
                            if (!int.TryParse(json.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
                                return false;
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default:
                            sb.Append(n);
                            break;
                    }
                    continue;
                }
                if (c == '"')
                {
                    text = sb.ToString();
                    return true;
                }
                sb.Append(c);
            }
            return false;
        }

        public static bool TryReadLong(string json, string key, int from, out long value, out int at)
        {
            value = 0;
            at = -1;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
            var needle = "\"" + key + "\"";
            var startAt = from < 0 ? 0 : from;
            if (startAt > json.Length) return false;
            var keyAt = json.IndexOf(needle, startAt, StringComparison.Ordinal);
            if (keyAt < 0) return false;
            var colon = json.IndexOf(':', keyAt + needle.Length);
            if (colon < 0) return false;
            var i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\n' || json[i] == '\r' || json[i] == '\t')) i++;
            if (i >= json.Length) return false;
            var start = i;
            if (json[i] == '-') i++;
            var digits = 0;
            while (i < json.Length && json[i] >= '0' && json[i] <= '9')
            {
                i++;
                digits++;
            }
            if (digits < 1 || digits > 18) return false;
            if (i < json.Length && (json[i] == '.' || json[i] == 'e' || json[i] == 'E')) return false;
            long parsed;
            if (!long.TryParse(json.Substring(start, i - start), out parsed)) return false;
            value = parsed;
            at = i;
            return true;
        }

        public static bool TryReadToken(string json, string key, int from, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
            var needle = "\"" + key + "\"";
            var startAt = from < 0 ? 0 : from;
            if (startAt > json.Length) return false;
            var keyAt = json.IndexOf(needle, startAt, StringComparison.Ordinal);
            if (keyAt < 0) return false;
            var colon = json.IndexOf(':', keyAt + needle.Length);
            if (colon < 0) return false;
            var i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\n' || json[i] == '\r' || json[i] == '\t')) i++;
            if (i >= json.Length || json[i] != '"') return false;
            i++;
            var start = i;
            while (i < json.Length)
            {
                var c = json[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
                if (!ok) break;
                i++;
                if (i - start > 8) return false;
            }
            if (i >= json.Length || json[i] != '"' || i == start) return false;
            value = json.Substring(start, i - start);
            return true;
        }

        public static bool TryObjectAfter(string json, string key, int from, out string body)
        {
            body = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
            var needle = "\"" + key + "\"";
            var startAt = from < 0 ? 0 : from;
            if (startAt > json.Length) return false;
            var keyAt = json.IndexOf(needle, startAt, StringComparison.Ordinal);
            if (keyAt < 0) return false;
            var brace = json.IndexOf('{', keyAt + needle.Length);
            if (brace < 0 || brace - keyAt > needle.Length + 8) return false;
            var depth = 0;
            for (var i = brace; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"')
                {
                    i++;
                    while (i < json.Length && json[i] != '"')
                    {
                        if (json[i] == '\\' && i + 1 < json.Length) i++;
                        i++;
                    }
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        body = json.Substring(brace, i - brace + 1);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
