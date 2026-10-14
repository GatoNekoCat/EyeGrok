// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Security;

namespace EyeGrok.Net
{
    public sealed class RelayResult
    {
        public bool Ok;
        public bool Miss;
        public bool Backoff;
        public string Text;
        public string Error;
        public int Requests;
    }

    public sealed class RelayClient
    {
        public const float FirstDelaySeconds = 2f;
        public const float IntervalSeconds = 1f;
        public const float DeadlineSeconds = 120f;
        public const int MaxRequests = 125;
        public const float MaxBackoffSeconds = 16f;

        public PingTrace Trace;

        readonly IHttpTransport _transport;
        readonly ISecretStore _store;
        readonly IClock _clock;

        public RelayClient(IHttpTransport transport, ISecretStore store, IClock clock)
        {
            _transport = transport;
            _store = store;
            _clock = clock;
        }

        public async Task<RelayResult> Poll(string nonce, CancellationToken ct)
        {
            var start = _clock.Now;
            var interval = IntervalSeconds;
            var requests = 0;
            var first = true;
            while (requests < MaxRequests)
            {
                var wait = first ? FirstDelaySeconds : interval;
                if (_clock.Now + wait - start > DeadlineSeconds)
                    break;
                await _clock.Wait(wait, ct);
                ct.ThrowIfCancellationRequested();
                if (_clock.Now - start > DeadlineSeconds)
                    break;
                requests++;
                var once = await Once(nonce, ct, requests);
                if (once.Ok)
                {
                    once.Requests = requests;
                    return once;
                }
                if (!once.Miss && once.Error != null && !once.Backoff)
                {
                    once.Requests = requests;
                    return once;
                }
                if (once.Backoff)
                    interval = Math.Min(interval * 2f, MaxBackoffSeconds);
                else
                    interval = IntervalSeconds;
                first = false;
            }
            return new RelayResult
            {
                Ok = false,
                Error = "no reply in 120 s. The reply may still arrive. Use Check again.",
                Requests = requests
            };
        }

        public async Task<RelayResult> CheckAgain(string nonce, CancellationToken ct)
        {
            var once = await Once(nonce, ct, 0);
            once.Requests = 1;
            if (once.Miss)
            {
                once.Ok = false;
                once.Error = "No reply yet";
            }
            return once;
        }

        async Task<RelayResult> Once(string nonce, CancellationToken ct, int pollIndex)
        {
            string url;
            string key;
            if (!_store.TryGet("relayUrl", out url) || !_store.TryGet("relayReadKey", out key))
                return Fatal("relay 0");
            var trace = Trace;
            if (trace != null && pollIndex > 0) trace.MarkPollSend(pollIndex);
            var response = await _transport.Send(new HttpRequest
            {
                Method = "GET",
                Url = url.TrimEnd('/') + "/reply/" + nonce,
                Authorization = "Bearer " + key,
                TimeoutSeconds = 10
            }, ct);
            if (trace != null && pollIndex > 0) trace.MarkPollRecv(pollIndex, response.Status, response.Body);
            if (response.NetworkError || response.Status == 0)
                return Backoff();
            if (response.Status == 200)
            {
                string text;
                if (!JsonBody.TryReadText(response.Body, out text))
                    return Fatal("relay parse");
                return new RelayResult { Ok = true, Text = text };
            }
            if (response.Status == 404)
                return new RelayResult { Miss = true };
            if (response.Status == 401 || response.Status == 400)
                return Fatal("relay " + response.Status);
            if (response.Status == 429 || response.Status >= 500)
                return Backoff();
            return Backoff();
        }

        static RelayResult Fatal(string error)
        {
            return new RelayResult { Error = error };
        }

        static RelayResult Backoff()
        {
            return new RelayResult { Miss = true, Backoff = true };
        }
    }
}
