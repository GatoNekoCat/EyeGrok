// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using EyeGrok.Security;

namespace EyeGrok.Config
{
    public static class SetupApplier
    {
        public static bool Apply(SetupData data, ISecretStore store)
        {
            if (data == null || store == null) return false;
            if (!PutQuiet(store, "webhookUrl", data.webhookUrl)) return false;
            if (!PutQuiet(store, "webhookKey", data.webhookKey)) return false;
            if (!PutQuiet(store, "relayUrl", data.relayUrl)) return false;
            if (!PutQuiet(store, "relayReadKey", data.relayReadKey)) return false;
            Redactor.Register(data.webhookKey);
            Redactor.Register(data.relayReadKey);
            Redactor.Register(SetupImporter.UrlPath(data.webhookUrl));
            Redactor.Register(SetupImporter.UrlPath(data.relayUrl));
            return true;
        }

        static bool PutQuiet(ISecretStore store, string name, string value)
        {
            try
            {
                return store.Put(name, value);
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
