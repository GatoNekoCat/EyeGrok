// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using EyeGrok.Config;

namespace EyeGrok.Pair
{
    public sealed class PairMaterial
    {
        public string RelayUrl;
        public byte[] Id;
        public byte[] Secret;
        public byte[] Enc;
        public byte[] Mac;
    }

    public static class PairPayload
    {
        public static bool TryParse(string text, string expectedRelay, bool allowLoopback, out PairMaterial material)
        {
            material = null;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split('|');
            if (parts.Length != 4) return false;
            if (parts[0] != "EG1") return false;
            var relay = parts[1];
            if (string.IsNullOrEmpty(relay) || relay.IndexOf('?') >= 0 || relay.IndexOf('#') >= 0) return false;
            if (!SetupData.IsHttpsOrLoopback(relay, allowLoopback)) return false;
            if (TrimSlash(relay) != TrimSlash(expectedRelay)) return false;
            var id = PairCrypto.FromB64Url(parts[2]);
            var secret = PairCrypto.FromB64Url(parts[3]);
            if (id == null || id.Length != 16) return false;
            if (secret == null || secret.Length != 32) return false;
            material = new PairMaterial
            {
                RelayUrl = relay,
                Id = id,
                Secret = secret,
                Enc = PairCrypto.Hkdf(secret, "enc", 32),
                Mac = PairCrypto.Hkdf(secret, "mac", 32)
            };
            return true;
        }

        public static string TrimSlash(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            return url.TrimEnd('/');
        }
    }
}
