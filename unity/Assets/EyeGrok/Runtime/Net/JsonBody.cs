// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

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
    }
}
