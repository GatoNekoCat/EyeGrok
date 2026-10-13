// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Config;
using EyeGrok.Net;
using EyeGrok.Security;
using UnityEngine;

namespace EyeGrok.Pair
{
    public enum PairResult
    {
        Ok,
        NotFound,
        RateLimited,
        PairingOff,
        BadCode,
        Corrupt,
        Network
    }

    public sealed class PairingClient
    {
        public const int MaxGets = 6;
        public const float RetrySeconds = 5f;

        readonly IHttpTransport _transport;
        readonly IClock _clock;
        readonly ISecretStore _store;

        public PairingClient(IHttpTransport transport, IClock clock, ISecretStore store)
        {
            _transport = transport;
            _clock = clock;
            _store = store;
        }

        public Task<PairResult> RedeemCode(string relayUrl, string code11, CancellationToken ct)
        {
            if (!PairCrypto.DammValid(code11))
            {
                Debug.Log("pair res=" + PairResult.BadCode);
                return Task.FromResult(PairResult.BadCode);
            }
            var master = PairCrypto.Pbkdf2(code11.Substring(0, 10));
            var idFull = PairCrypto.Hkdf(master, "id", 32);
            var id = new byte[16];
            Buffer.BlockCopy(idFull, 0, id, 0, 16);
            var enc = PairCrypto.Hkdf(master, "enc", 32);
            var mac = PairCrypto.Hkdf(master, "mac", 32);
            return Redeem(relayUrl, id, enc, mac, ct);
        }

        public Task<PairResult> RedeemQr(string text, string expectedRelay, bool allowLoopback, CancellationToken ct)
        {
            PairMaterial material;
            if (!PairPayload.TryParse(text, expectedRelay, allowLoopback, out material))
            {
                Debug.Log("pair res=" + PairResult.BadCode);
                return Task.FromResult(PairResult.BadCode);
            }
            return Redeem(material.RelayUrl, material.Id, material.Enc, material.Mac, ct);
        }

        public async Task<PairResult> Redeem(string relayUrl, byte[] id, byte[] enc, byte[] mac, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(relayUrl) || id == null || enc == null || mac == null)
            {
                Debug.Log("pair res=" + PairResult.BadCode);
                return PairResult.BadCode;
            }
            var url = relayUrl.TrimEnd('/') + "/pair/" + PairCrypto.B64Url(id);
            for (var attempt = 1; attempt <= MaxGets; attempt++)
            {
                HttpResponse response;
                try
                {
                    response = await _transport.Send(new HttpRequest
                    {
                        Method = "GET",
                        Url = url,
                        TimeoutSeconds = 20
                    }, ct);
                }
                catch (Exception ex)
                {
                    Debug.Log("pair err=" + ex.GetType().Name);
                    Debug.Log("pair res=" + PairResult.Network);
                    return PairResult.Network;
                }
                if (response == null || response.NetworkError || response.Status == 0)
                {
                    Debug.Log("pair res=" + PairResult.Network);
                    return PairResult.Network;
                }
                if (response.Status == 429)
                {
                    Debug.Log("pair res=" + PairResult.RateLimited);
                    return PairResult.RateLimited;
                }
                if (response.Status == 503)
                {
                    Debug.Log("pair res=" + PairResult.PairingOff);
                    return PairResult.PairingOff;
                }
                if (response.Status == 404)
                {
                    if (attempt == MaxGets)
                    {
                        Debug.Log("pair res=" + PairResult.NotFound);
                        return PairResult.NotFound;
                    }
                    await _clock.Wait(RetrySeconds, ct);
                    continue;
                }
                if (response.Status != 200)
                {
                    Debug.Log("pair res=" + PairResult.Network);
                    return PairResult.Network;
                }
                var result = ApplyBody(response.Body, id, enc, mac);
                if (result == PairResult.Ok) Debug.Log("pair ok");
                else Debug.Log("pair res=" + result);
                return result;
            }
            Debug.Log("pair res=" + PairResult.NotFound);
            return PairResult.NotFound;
        }

        PairResult ApplyBody(string body, byte[] id, byte[] enc, byte[] mac)
        {
            BoxResponse parsed;
            try
            {
                parsed = JsonUtility.FromJson<BoxResponse>(body);
            }
            catch (Exception ex)
            {
                Debug.Log("pair err=" + ex.GetType().Name);
                return PairResult.Corrupt;
            }
            if (parsed == null || parsed.box == null || parsed.box.v != 1) return PairResult.Corrupt;
            var iv = PairCrypto.FromB64Url(parsed.box.iv);
            var ct = PairCrypto.FromB64Url(parsed.box.ct);
            var tag = PairCrypto.FromB64Url(parsed.box.mac);
            if (iv == null || iv.Length != 16 || ct == null || tag == null || tag.Length != 32) return PairResult.Corrupt;
            var plain = PairCrypto.Unprotect(iv, ct, tag, enc, mac, id);
            if (plain == null) return PairResult.Corrupt;
            var json = System.Text.Encoding.UTF8.GetString(plain);
            SetupData data;
            string field;
            if (!SetupData.TryParse(json, true, out data, out field)) return PairResult.Corrupt;
            if (!SetupApplier.Apply(data, _store)) return PairResult.Corrupt;
            return PairResult.Ok;
        }

        [Serializable]
        sealed class BoxResponse
        {
            public BoxBody box;
        }

        [Serializable]
        sealed class BoxBody
        {
            public int v;
            public string iv;
            public string ct;
            public string mac;
        }
    }
}
