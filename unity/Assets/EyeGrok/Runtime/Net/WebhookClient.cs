// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Security;

namespace EyeGrok.Net
{
    public sealed class WebhookResult
    {
        public bool Ok;
        public string Error;
    }

    public sealed class WebhookClient
    {
        readonly IHttpTransport _transport;
        readonly ISecretStore _store;

        public WebhookClient(IHttpTransport transport, ISecretStore store)
        {
            _transport = transport;
            _store = store;
        }

        public async Task<WebhookResult> Send(string text, string nonce, CancellationToken ct)
        {
            string url;
            string key;
            if (!_store.TryGet("webhookUrl", out url) || !_store.TryGet("webhookKey", out key))
                return Fail("webhook 0");
            var response = await _transport.Send(new HttpRequest
            {
                Method = "POST",
                Url = url,
                Body = JsonBody.TextAndNonce(text, nonce),
                Authorization = "Bearer " + key,
                ContentType = "application/json",
                TimeoutSeconds = 15
            }, ct);
            if (response.NetworkError || response.Status == 0)
                return Fail("webhook 0");
            if (response.Status == 200)
                return new WebhookResult { Ok = true };
            return Fail("webhook " + response.Status);
        }

        static WebhookResult Fail(string error)
        {
            return new WebhookResult { Ok = false, Error = error };
        }
    }
}
