// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EyeGrok.Security
{
    public static class Redactor
    {
        static readonly List<string> Secrets = new List<string>();
        static readonly object Gate = new object();
        static readonly Regex Bearer = new Regex("Bearer\\s+\\S+", RegexOptions.Compiled);
        static readonly Regex UrlPath = new Regex("https?://[^/\\s]+/\\S*", RegexOptions.Compiled);

        public static void Clear()
        {
            lock (Gate) Secrets.Clear();
        }

        public static void Register(string secret)
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 4) return;
            lock (Gate)
            {
                if (!Secrets.Contains(secret)) Secrets.Add(secret);
            }
        }

        public static string Redact(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string[] copy;
            lock (Gate)
            {
                copy = Secrets.ToArray();
            }
            for (var i = 0; i < copy.Length; i++)
            {
                if (!string.IsNullOrEmpty(copy[i]))
                    value = value.Replace(copy[i], "[REDACTED]");
            }
            value = Bearer.Replace(value, "Bearer [REDACTED]");
            value = UrlPath.Replace(value, delegate(Match match)
            {
                var text = match.Value;
                var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
                var scheme = schemeEnd > 0 ? text.Substring(0, schemeEnd + 3) : "";
                var rest = text.Substring(scheme.Length);
                var slash = rest.IndexOf('/');
                var host = slash >= 0 ? rest.Substring(0, slash) : rest;
                return scheme + host + "/[\u2026]";
            });
            return value;
        }
    }
}
