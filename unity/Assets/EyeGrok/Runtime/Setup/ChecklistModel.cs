// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

using System.Threading;
using System.Threading.Tasks;
using EyeGrok.Net;
using EyeGrok.Ping;

namespace EyeGrok.Setup
{
    public sealed class ChecklistModel
    {
        public string Mic = "red";
        public string Relay = "red";
        public string Routine = "not tested";

        public void EvaluateMic(bool authorized, int devices)
        {
            if (authorized && devices > 0) Mic = "green";
            else Mic = "red";
        }

        public async Task EvaluateRelay(IHttpTransport transport, string relayUrl, CancellationToken ct)
        {
            Relay = "red";
            if (transport == null || string.IsNullOrEmpty(relayUrl)) return;
            var root = relayUrl.TrimEnd('/');
            var response = await transport.Send(new HttpRequest
            {
                Method = "GET",
                Url = root + "/health",
                TimeoutSeconds = 20
            }, ct);
            if (response != null && !response.NetworkError && response.Status == 200 && response.Body != null && response.Body.Trim() == "ok")
                Relay = "green";
        }

        public void NoteRoutine(PingState state)
        {
            if (state == PingState.Done) Routine = "green";
            else if (state == PingState.Error) Routine = "red";
        }
    }
}
