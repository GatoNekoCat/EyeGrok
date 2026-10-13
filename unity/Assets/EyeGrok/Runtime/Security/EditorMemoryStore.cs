// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using EyeGrok.Config;

namespace EyeGrok.Security
{
    public sealed class EditorMemoryStore : ISecretStore
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public EditorMemoryStore()
        {
            var path = Environment.GetEnvironmentVariable("EYEGROK_SETUP_FILE");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return;
            }
            SetupData data;
            string field;
            if (!SetupData.TryParse(json, true, out data, out field)) return;
            Put("webhookUrl", data.webhookUrl);
            Put("webhookKey", data.webhookKey);
            Put("relayUrl", data.relayUrl);
            Put("relayReadKey", data.relayReadKey);
        }

        public void Put(string name, string value)
        {
            _values[name] = value;
        }

        public bool TryGet(string name, out string value)
        {
            return _values.TryGetValue(name, out value) && !string.IsNullOrEmpty(value);
        }

        public void DeleteAll()
        {
            _values.Clear();
        }
    }

    public static class SecretStores
    {
        public static ISecretStore Create()
        {
#if UNITY_EDITOR
            return new EditorMemoryStore();
#else
            return new AndroidKeystoreStore();
#endif
        }
    }
}
