// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using UnityEngine;

namespace EyeGrok.Security
{
    public sealed class AndroidKeystoreStore : ISecretStore
    {
        public const string Alias = "eyegrok_v1";

        public bool Put(string name, string value)
        {
            return WithStore((activity, store) => store.CallStatic("put", activity, Alias, name, value));
        }

        public bool TryGet(string name, out string value)
        {
            value = null;
            string got = null;
            var ok = WithStore((activity, store) =>
            {
                got = store.CallStatic<string>("get", activity, Alias, name);
            });
            if (!ok || string.IsNullOrEmpty(got)) return false;
            value = got;
            return true;
        }

        public void DeleteAll()
        {
            WithStore((activity, store) => store.CallStatic("deleteAlias", activity, Alias));
        }

        public static string ExternalFilesDir()
        {
            string dir = null;
            WithStore((activity, store) => { dir = store.CallStatic<string>("externalFilesDir", activity); });
            return dir ?? "";
        }

        static bool WithStore(Action<AndroidJavaObject, AndroidJavaClass> call)
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var store = new AndroidJavaClass("io.github.eyegrok.EyeGrokKeystore"))
                    call(activity, store);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Log("keystore err=" + ex.GetType().Name);
                return false;
            }
        }
    }
}
