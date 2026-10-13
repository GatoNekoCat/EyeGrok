// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using EyeGrok.Security;

namespace EyeGrok.Config
{
    public struct ImportOutcome
    {
        public bool Found;
        public bool Ok;
        public string Message;
    }

    public static class SetupImporter
    {
        public const int MaxBytes = 4096;
        public const string FileName = "eyegrok-setup.json";

        public static ImportOutcome TryImport(string path, ISecretStore store, bool allowLoopbackHttp)
        {
            var outcome = new ImportOutcome();
            if (string.IsNullOrEmpty(path) || store == null || !File.Exists(path)) return outcome;
            outcome.Found = true;
            string json = null;
            string fail = null;
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxBytes)
                    fail = "json";
                else
                    json = File.ReadAllText(path);
            }
            catch (Exception)
            {
                fail = "json";
            }
            finally
            {
                TryDelete(path);
            }

            if (fail != null)
            {
                outcome.Message = "Setup file invalid: " + fail;
                return outcome;
            }

            SetupData data;
            string field;
            if (!SetupData.TryParse(json, allowLoopbackHttp, out data, out field))
            {
                outcome.Message = "Setup file invalid: " + field;
                return outcome;
            }

            store.Put("webhookUrl", data.webhookUrl);
            store.Put("webhookKey", data.webhookKey);
            store.Put("relayUrl", data.relayUrl);
            store.Put("relayReadKey", data.relayReadKey);
            Redactor.Register(data.webhookKey);
            Redactor.Register(data.relayReadKey);
            Redactor.Register(UrlPath(data.webhookUrl));
            Redactor.Register(UrlPath(data.relayUrl));
            outcome.Ok = true;
            outcome.Message = "Setup imported";
            return outcome;
        }

        public static string UrlPath(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            var mark = url.IndexOf("://", StringComparison.Ordinal);
            if (mark < 0) return "";
            var rest = url.Substring(mark + 3);
            var slash = rest.IndexOf('/');
            if (slash < 0 || slash >= rest.Length - 1) return "";
            return rest.Substring(slash);
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
            }
        }
    }
}
