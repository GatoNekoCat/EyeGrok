// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace EyeGrok.Config
{
    [Serializable]
    public sealed class SetupData
    {
        public string webhookUrl;
        public string webhookKey;
        public string relayUrl;
        public string relayReadKey;

        public static bool TryParse(string json, bool allowLoopbackHttp, out SetupData data, out string field)
        {
            data = null;
            field = "json";
            if (string.IsNullOrEmpty(json)) return false;
            SetupData parsed;
            try
            {
                parsed = JsonUtility.FromJson<SetupData>(json);
            }
            catch (Exception)
            {
                return false;
            }
            if (parsed == null) return false;
            if (!IsHttpsOrLoopback(parsed.webhookUrl, allowLoopbackHttp))
            {
                field = "webhookUrl";
                return false;
            }
            if (!IsKey(parsed.webhookKey))
            {
                field = "webhookKey";
                return false;
            }
            if (!IsHttpsOrLoopback(parsed.relayUrl, allowLoopbackHttp))
            {
                field = "relayUrl";
                return false;
            }
            if (!IsKey(parsed.relayReadKey))
            {
                field = "relayReadKey";
                return false;
            }
            data = parsed;
            field = null;
            return true;
        }

        public static bool IsHttpsOrLoopback(string url, bool allowLoopbackHttp)
        {
            if (string.IsNullOrEmpty(url) || url.Length > 2048) return false;
            if (url.StartsWith("https://", StringComparison.Ordinal) && url.Length > "https://".Length)
                return true;
#if UNITY_EDITOR
            if (!allowLoopbackHttp) return false;
            return url.StartsWith("http://127.0.0.1", StringComparison.Ordinal)
                || url.StartsWith("http://localhost", StringComparison.Ordinal);
#else
            return allowLoopbackHttp && false;
#endif
        }

        public static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 16 || key.Length > 512) return false;
            for (var i = 0; i < key.Length; i++)
            {
                if (char.IsWhiteSpace(key[i])) return false;
            }
            return true;
        }
    }
}
